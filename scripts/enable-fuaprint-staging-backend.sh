#!/usr/bin/env bash
set -euo pipefail

[[ ${EUID} -eq 0 ]] || { echo "ERROR: run as root" >&2; exit 77; }

env_file=/etc/fuapay-staging/staging.env
service=fuapay-staging.service
source_ip=147.230.21.129
port=8443

for command in python3 systemctl ufw curl stat install mktemp; do
    command -v "${command}" >/dev/null 2>&1 || { echo "ERROR: missing ${command}" >&2; exit 69; }
done
[[ -f "${env_file}" && ! -L "${env_file}" ]] || { echo "ERROR: staging env missing/unsafe" >&2; exit 66; }

meta=$(stat -c '%U:%G:%a' "${env_file}")
owner=${meta%%:*}
rest=${meta#*:}
group=${rest%%:*}
mode=${meta##*:}
[[ ${owner} == root && ${group} == fuapay-staging ]] || { echo "ERROR: staging env ownership changed: ${meta}" >&2; exit 68; }

backup=$(mktemp)
candidate=$(mktemp)
ready_file=$(mktemp)
cp -a "${env_file}" "${backup}"
was_active=0
was_enabled=0
rule_existed=0
systemctl is-active --quiet "${service}" && was_active=1
systemctl is-enabled --quiet "${service}" 2>/dev/null && was_enabled=1
LC_ALL=C ufw status | grep -F "${port}/tcp" | grep -Fq "${source_ip}" && rule_existed=1 || true

cleanup(){ rm -f -- "${backup}" "${candidate}" "${ready_file}"; }
trap cleanup EXIT

python3 - "${env_file}" "${candidate}" <<'PY'
import base64
import re
import sys
import uuid
from pathlib import Path

source = Path(sys.argv[1])
target = Path(sys.argv[2])
lines = source.read_text(encoding="utf-8").splitlines()
values = {}
for line in lines:
    stripped = line.strip()
    if not stripped or stripped.startswith("#") or "=" not in stripped:
        continue
    key, value = stripped.split("=", 1)
    values[key] = value.strip().strip('"')

required_exact = {
    "Payments__Provider": "None",
    "Csob__Enabled": "false",
    "StagingTestMode__SimulatedPaymentsEnabled": "false",
    "Database__ApplyMigrationsOnStart": "false",
}
for key, expected in required_exact.items():
    if values.get(key) != expected:
        raise SystemExit(f"ERROR: staging safety key {key} is {values.get(key)!r}, expected {expected!r}")

source_id = values.get("PrintPayments__Sources__0__PrintSourceId", "")
digest = values.get("PrintPayments__Sources__0__CredentialSha256", "")
pepper = values.get("PrintCredentials__PepperBase64", "")
try:
    uuid.UUID(source_id)
except (ValueError, AttributeError) as exc:
    raise SystemExit("ERROR: staging FUA Print source ID is missing/invalid") from exc
if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
    raise SystemExit("ERROR: staging FUA Print service digest is missing/invalid")
try:
    raw = base64.b64decode(pepper, validate=True)
except Exception as exc:
    raise SystemExit("ERROR: staging print credential pepper is invalid Base64") from exc
if len(raw) < 32:
    raise SystemExit("ERROR: staging print credential pepper is too short")

updates = {
    "PrintPayments__Enabled": "true",
    "PrintCredentials__Enabled": "true",
}
seen = set()
out = []
for line in lines:
    stripped = line.strip()
    if stripped and not stripped.startswith("#") and "=" in stripped:
        key = stripped.split("=", 1)[0]
        if key in updates:
            prefix = line[:len(line) - len(line.lstrip())]
            out.append(prefix + key + "=" + updates[key])
            seen.add(key)
            continue
    out.append(line)
if seen != set(updates):
    missing = sorted(set(updates) - seen)
    raise SystemExit("ERROR: staging print feature keys missing: " + ",".join(missing))
target.write_text("\n".join(out) + "\n", encoding="utf-8")
PY

rollback(){
    code=$?
    trap - ERR
    set +e
    install -o "${owner}" -g "${group}" -m "${mode}" "${backup}" "${env_file}"
    if [[ ${was_active} -eq 1 ]]; then systemctl restart "${service}" >/dev/null 2>&1; else systemctl stop "${service}" >/dev/null 2>&1; fi
    [[ ${was_enabled} -eq 1 ]] || systemctl disable "${service}" >/dev/null 2>&1
    if [[ ${rule_existed} -eq 0 ]]; then ufw --force delete allow from "${source_ip}" to any port "${port}" proto tcp >/dev/null 2>&1 || true; fi
    echo "ERROR: FUA Pay staging print backend activation failed; prior state restored" >&2
    exit "${code}"
}
trap rollback ERR

install -o "${owner}" -g "${group}" -m "${mode}" "${candidate}" "${env_file}"
if [[ ${rule_existed} -eq 0 ]]; then
    ufw allow from "${source_ip}" to any port "${port}" proto tcp
fi
systemctl enable --now "${service}"

ready=0
for _ in {1..80}; do
    code=$(curl --silent --show-error --output ${ready_file} --write-out '%{http_code}' --resolve fuapay.fa.tul.cz:8443:127.0.0.1 https://fuapay.fa.tul.cz:8443/health/ready || true)
    if [[ ${code} == 200 ]] && grep -qi 'healthy' ${ready_file}; then ready=1; break; fi
    sleep 0.25
done
rm -f -- ${ready_file}
[[ ${ready} -eq 1 ]] || { echo "ERROR: staging readiness did not become Healthy" >&2; false; }

LC_ALL=C ufw status | grep -F "${port}/tcp" | grep -F "${source_ip}" >/dev/null || { echo "ERROR: source-restricted 8443 firewall rule missing" >&2; false; }
systemctl is-active --quiet "${service}" || { echo "ERROR: staging service not active" >&2; false; }
systemctl is-enabled --quiet "${service}" || { echo "ERROR: staging service not enabled" >&2; false; }

trap - ERR
echo
echo "=== FUA PAY STAGING PRINT BACKEND: PASS ==="
echo "service:     active + enabled"
echo "edge:        https://fuapay.fa.tul.cz:8443"
echo "firewall:    ${source_ip} -> ${port}/tcp only"
echo "print:       PrintPayments + PrintCredentials enabled"
echo "card/CSOB:   disabled"
echo "simulation:  disabled"
echo "secrets:     validated, not printed"
