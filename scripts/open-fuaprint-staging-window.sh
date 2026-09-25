#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "ERROR: run as root" >&2
    exit 77
fi

for command in systemctl sha256sum stat readlink ss curl ufw install grep awk cut mktemp python3; do
    command -v "${command}" >/dev/null 2>&1 || {
        echo "ERROR: required command missing: ${command}" >&2
        exit 69
    }
done

readonly expected_baseline_sha="3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4"
readonly expected_prod_nginx_sha="491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9"
readonly expected_source_id="52b7cf47-1157-457a-8f21-a217fc1cc0d8"
readonly expected_digest="2b4451ab0a30da6d96feb3af8fd76efff619afaef1c5b98ad1a230f768700392"
readonly client_ip="147.230.21.129"

readonly prod_env="/etc/fuapay/production.env"
readonly prod_site="/etc/nginx/sites-available/fuapay"
readonly prod_current="/opt/fuapay/current"
readonly staging_env="/etc/fuapay-staging/staging.env"
readonly historical_env="/etc/fuapay/staging.env"
readonly staging_service="fuapay-staging.service"
readonly state_dir="/var/lib/fuapay-staging/acceptance"
readonly state_file="${state_dir}/fuaprint-window.state"

require_regular_file() {
    local path="$1"
    local label="$2"
    if [[ ! -f "${path}" || -L "${path}" ]]; then
        echo "ERROR: ${label} must be a regular non-symlink file: ${path}" >&2
        exit 66
    fi
}

read_one() {
    local key="$1"
    local file="$2"
    local count
    count=$(grep -c "^${key}=" "${file}" || true)
    if [[ "${count}" -ne 1 ]]; then
        echo "ERROR: expected exactly one ${key} in ${file}; found ${count}" >&2
        exit 65
    fi
    grep "^${key}=" "${file}" | cut -d= -f2-
}

production_health_ok() {
    local value
    value=$(curl --fail --silent --show-error --max-time 10 https://fuapay.tul.cz/health/ready)
    grep -Fq "Healthy" <<<"${value}"
}

production_alias_ok() {
    local value
    value=$(curl --silent --show-error --output /dev/null --write-out '%{http_code}|%{redirect_url}' --max-time 10 https://fuapay.fa.tul.cz/)
    [[ "${value}" == "301|https://fuapay.tul.cz/" ]]
}

production_listener_ok() {
    local listener
    listener=$(ss -ltnH | awk '$4 ~ /:5080$/ {print $4}' | sort -u)
    [[ "${listener}" == "127.0.0.1:5080" ]]
}

assert_production_live() {
    systemctl is-active --quiet fuapay.service || {
        echo "ERROR: production fuapay.service is not active" >&2
        return 1
    }
    systemctl is-enabled --quiet fuapay.service || {
        echo "ERROR: production fuapay.service is not enabled" >&2
        return 1
    }
    production_listener_ok || {
        echo "ERROR: production listener is not exactly 127.0.0.1:5080" >&2
        return 1
    }
    production_health_ok || {
        echo "ERROR: production /health/ready is not Healthy" >&2
        return 1
    }
    production_alias_ok || {
        echo "ERROR: production alias is not the expected 301 redirect" >&2
        return 1
    }
}

require_regular_file "${prod_env}" "production env"
require_regular_file "${prod_site}" "production nginx site"
require_regular_file "${staging_env}" "staging env"
require_regular_file "${historical_env}" "historical staging env"
[[ -L "${prod_current}" ]] || {
    echo "ERROR: production current symlink is missing" >&2
    exit 66
}

assert_production_live

prod_env_sha_before=$(sha256sum "${prod_env}" | awk '{print $1}')
prod_site_sha_before=$(sha256sum "${prod_site}" | awk '{print $1}')
prod_current_before=$(readlink -f "${prod_current}")

if [[ "${prod_site_sha_before}" != "${expected_prod_nginx_sha}" ]]; then
    echo "ERROR: production nginx site hash differs from documented accepted baseline" >&2
    echo "ACTUAL_PRODUCTION_NGINX_SHA256=${prod_site_sha_before}" >&2
    exit 71
fi

assert_production_unchanged() {
    local prod_env_sha_now prod_site_sha_now prod_current_now
    prod_env_sha_now=$(sha256sum "${prod_env}" | awk '{print $1}')
    prod_site_sha_now=$(sha256sum "${prod_site}" | awk '{print $1}')
    prod_current_now=$(readlink -f "${prod_current}")

    [[ "${prod_env_sha_now}" == "${prod_env_sha_before}" ]] || {
        echo "ERROR: production env changed" >&2
        return 1
    }
    [[ "${prod_site_sha_now}" == "${prod_site_sha_before}" ]] || {
        echo "ERROR: production nginx site changed" >&2
        return 1
    }
    [[ "${prod_current_now}" == "${prod_current_before}" ]] || {
        echo "ERROR: production current symlink changed" >&2
        return 1
    }
    assert_production_live
}

baseline_sha=$(sha256sum "${staging_env}" | awk '{print $1}')
if [[ "${baseline_sha}" != "${expected_baseline_sha}" ]]; then
    echo "ERROR: active staging env is not the documented fail-closed baseline" >&2
    echo "ACTUAL_STAGING_ENV_SHA256=${baseline_sha}" >&2
    exit 65
fi

metadata=$(stat -c '%U:%G:%a' "${staging_env}")
if [[ "${metadata}" != "root:fuapay-staging:640" ]]; then
    echo "ERROR: unexpected staging env metadata: ${metadata}" >&2
    exit 68
fi

if systemctl is-active --quiet "${staging_service}"; then
    echo "ERROR: staging service is already active" >&2
    exit 73
fi
if systemctl is-enabled --quiet "${staging_service}"; then
    echo "ERROR: staging service must remain disabled outside an acceptance window" >&2
    exit 73
fi

staging_listener=$(ss -ltnH | awk '$4 ~ /:5081$/ {print $4}' | sort -u)
if [[ -n "${staging_listener}" ]]; then
    echo "ERROR: staging backend 5081 is already listening: ${staging_listener}" >&2
    exit 73
fi

if ufw status | grep -q '8443'; then
    echo "ERROR: an existing UFW rule mentions 8443; refusing to broaden/replace it" >&2
    ufw status | grep '8443' >&2 || true
    exit 73
fi

install -d -o root -g root -m 0700 "${state_dir}"
if [[ -e "${state_file}" ]]; then
    echo "ERROR: an existing FUA Print staging-window state file exists: ${state_file}" >&2
    exit 73
fi

source_id=$(read_one 'PrintPayments__Sources__0__PrintSourceId' "${historical_env}")
digest=$(read_one 'PrintPayments__Sources__0__CredentialSha256' "${historical_env}")
pepper=$(read_one 'PrintCredentials__PepperBase64' "${historical_env}")

[[ "${source_id}" == "${expected_source_id}" ]] || {
    echo "ERROR: historical PrintSourceId differs from the accepted FUA Print source" >&2
    exit 65
}
[[ "${digest}" == "${expected_digest}" ]] || {
    echo "ERROR: historical service credential digest differs from the accepted digest" >&2
    exit 65
}
[[ -n "${pepper}" ]] || {
    echo "ERROR: historical PrintCredentials pepper is empty" >&2
    exit 65
}

python3 - "${pepper}" <<'PY'
import base64
import sys
try:
    value = base64.b64decode(sys.argv[1], validate=True)
except Exception as exc:
    raise SystemExit("ERROR: historical PrintCredentials pepper is not valid Base64") from exc
if len(value) < 32:
    raise SystemExit("ERROR: historical PrintCredentials pepper decodes to fewer than 32 bytes")
PY

stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup_dir="/var/backups/fuapay-staging/fuaprint-window-${stamp}"
install -d -o root -g root -m 0700 "${backup_dir}"
install -o root -g root -m 0600 "${staging_env}" "${backup_dir}/staging.env.before"
sha256sum "${backup_dir}/staging.env.before" >"${backup_dir}/staging.env.before.sha256"
chmod 0600 "${backup_dir}/staging.env.before.sha256"

candidate=$(mktemp /etc/fuapay-staging/.staging.env.fuaprint.XXXXXX)
ufw_added=0
env_replaced=0

rollback_on_error() {
    local code=$?
    trap - ERR INT TERM
    set +e
    echo "ERROR: opening FUA Print staging window failed; restoring staging baseline" >&2

    if [[ ${ufw_added} -eq 1 ]]; then
        ufw --force delete allow from "${client_ip}" to any port 8443 proto tcp >/dev/null 2>&1 || true
    fi

    systemctl stop "${staging_service}" >/dev/null 2>&1 || true
    systemctl disable "${staging_service}" >/dev/null 2>&1 || true

    if [[ ${env_replaced} -eq 1 && -f "${backup_dir}/staging.env.before" ]]; then
        install -o root -g fuapay-staging -m 0640 "${backup_dir}/staging.env.before" "${staging_env}" || true
    fi

    rm -f "${candidate}" >/dev/null 2>&1 || true
    rm -f "${state_file}" >/dev/null 2>&1 || true

    if assert_production_unchanged; then
        echo "PRODUCTION_UNCHANGED_AFTER_ROLLBACK=PASS" >&2
    else
        echo "CRITICAL: production guard no longer matches the pre-window snapshot" >&2
    fi

    exit "${code}"
}
trap rollback_on_error ERR INT TERM

grep -vE '^(PrintPayments__Enabled|PrintCredentials__Enabled|PrintPayments__Sources__[^=]+|PrintCredentials__PepperBase64)='     "${staging_env}" >"${candidate}"

cat >>"${candidate}" <<EOF2
PrintPayments__Enabled=true
PrintCredentials__Enabled=true
PrintPayments__Sources__0__PrintSourceId=${source_id}
PrintPayments__Sources__0__CredentialSha256=${digest}
PrintCredentials__PepperBase64=${pepper}
EOF2

chown root:fuapay-staging "${candidate}"
chmod 0640 "${candidate}"

for key in     PrintPayments__Enabled     PrintCredentials__Enabled     PrintPayments__Sources__0__PrintSourceId     PrintPayments__Sources__0__CredentialSha256     PrintCredentials__PepperBase64
do
    count=$(grep -c "^${key}=" "${candidate}" || true)
    [[ "${count}" -eq 1 ]] || {
        echo "ERROR: candidate env contains ${count} instances of ${key}" >&2
        false
    }
done

install -o root -g fuapay-staging -m 0640 "${candidate}" "${staging_env}"
env_replaced=1
rm -f "${candidate}"

printf '%s
' "${backup_dir}" >"${state_file}"
chown root:root "${state_file}"
chmod 0600 "${state_file}"

systemctl start "${staging_service}"
systemctl is-active --quiet "${staging_service}"
if systemctl is-enabled --quiet "${staging_service}"; then
    echo "ERROR: staging service became enabled unexpectedly" >&2
    false
fi

healthy=0
for _ in $(seq 1 40); do
    if curl --fail --silent --show-error --max-time 2         -H 'Host: fuapay.fa.tul.cz'         http://127.0.0.1:5081/health/ready 2>/dev/null | grep -Fq 'Healthy'; then
        healthy=1
        break
    fi
    sleep 0.25
done
[[ ${healthy} -eq 1 ]] || {
    echo "ERROR: staging /health/ready did not become Healthy" >&2
    false
}

ufw allow from "${client_ip}" to any port 8443 proto tcp comment 'FUA Print Paid-v2 acceptance'
ufw_added=1

ufw status | grep '8443' | grep -Fq "${client_ip}" || {
    echo "ERROR: expected source-scoped 8443 rule is not visible after add" >&2
    false
}

assert_production_unchanged
trap - ERR INT TERM

new_env_sha=$(sha256sum "${staging_env}" | awk '{print $1}')

echo
echo "=== FUA PAY STAGING WINDOW FOR FUA PRINT: OPEN ==="
echo "staging service: active / disabled"
echo "staging backend: 127.0.0.1:5081"
echo "staging HTTPS:   8443 allowed only from ${client_ip}"
echo "PrintPayments:   enabled only in staging"
echo "PrintCredentials: enabled only in staging"
echo "source ID:       expected match"
echo "service digest:  expected match"
echo "pepper:          present/validated, not printed"
echo "baseline backup: ${backup_dir}"
echo "baseline SHA256: ${expected_baseline_sha}"
echo "active env SHA:  ${new_env_sha}"
echo "production:      unchanged / Healthy"
echo
echo "PASS: isolated FUA Pay staging is ready for the FUA Print authenticated read-only probe."
