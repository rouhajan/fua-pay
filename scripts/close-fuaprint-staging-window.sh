#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "ERROR: run as root" >&2
    exit 77
fi

for command in systemctl sha256sum stat readlink ss curl ufw install grep awk runuser psql; do
    command -v "${command}" >/dev/null 2>&1 || {
        echo "ERROR: required command missing: ${command}" >&2
        exit 69
    }
done

readonly expected_baseline_sha="3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4"
readonly expected_prod_nginx_sha="491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9"
readonly expected_source_id="52b7cf47-1157-457a-8f21-a217fc1cc0d8"
readonly client_ip="147.230.21.129"

readonly prod_env="/etc/fuapay/production.env"
readonly prod_site="/etc/nginx/sites-available/fuapay"
readonly prod_current="/opt/fuapay/current"
readonly staging_env="/etc/fuapay-staging/staging.env"
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
[[ -L "${prod_current}" ]] || {
    echo "ERROR: production current symlink is missing" >&2
    exit 66
}
require_regular_file "${state_file}" "FUA Print staging-window state"

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

backup_dir=$(cat "${state_file}")
case "${backup_dir}" in
    /var/backups/fuapay-staging/fuaprint-window-*) ;;
    *)
        echo "ERROR: unsafe backup path recorded in state file" >&2
        exit 65
        ;;
esac

require_regular_file "${backup_dir}/staging.env.before" "staging baseline backup"
require_regular_file "${backup_dir}/staging.env.before.sha256" "staging baseline backup hash"

backup_sha=$(sha256sum "${backup_dir}/staging.env.before" | awk '{print $1}')
recorded_sha=$(awk '{print $1}' "${backup_dir}/staging.env.before.sha256")
[[ "${backup_sha}" == "${recorded_sha}" ]] || {
    echo "ERROR: staging baseline backup hash mismatch" >&2
    exit 71
}
[[ "${backup_sha}" == "${expected_baseline_sha}" ]] || {
    echo "ERROR: staging baseline backup is not the accepted fail-closed env" >&2
    exit 71
}

source_id_count=$(grep -c '^PrintPayments__Sources__0__PrintSourceId=' "${staging_env}" || true)
[[ "${source_id_count}" -eq 1 ]] || {
    echo "ERROR: active staging env does not contain exactly one PrintSourceId" >&2
    exit 65
}
source_id=$(grep '^PrintPayments__Sources__0__PrintSourceId=' "${staging_env}" | cut -d= -f2-)
[[ "${source_id}" == "${expected_source_id}" ]] || {
    echo "ERROR: active staging env PrintSourceId differs from accepted FUA Print source" >&2
    exit 65
}
grep -qx 'PrintPayments__Enabled=true' "${staging_env}" || {
    echo "ERROR: active staging env does not have PrintPayments enabled" >&2
    exit 65
}
grep -qx 'PrintCredentials__Enabled=true' "${staging_env}" || {
    echo "ERROR: active staging env does not have PrintCredentials enabled" >&2
    exit 65
}

active_reservations=$(runuser -u postgres -- psql -d fuapay_staging -Atc     "select count(*) from credits.print_reservations where print_source_id='${expected_source_id}'::uuid and status in (1,2);")
if [[ ! "${active_reservations}" =~ ^[0-9]+$ ]]; then
    echo "ERROR: unable to read active print reservation count" >&2
    exit 70
fi
if [[ "${active_reservations}" -ne 0 ]]; then
    echo "ERROR: ${active_reservations} Reserved/ResolutionRequired FUA Print reservation(s) remain; staging left open" >&2
    exit 75
fi

if ! ufw status | grep '8443' | grep -Fq "${client_ip}"; then
    echo "ERROR: expected source-scoped 8443 rule is absent; refusing ambiguous closeout" >&2
    exit 73
fi

systemctl stop "${staging_service}"
systemctl is-active --quiet "${staging_service}" && {
    echo "ERROR: staging service is still active after stop" >&2
    exit 70
}
if systemctl is-enabled --quiet "${staging_service}"; then
    echo "ERROR: staging service unexpectedly became enabled" >&2
    exit 73
fi

ufw --force delete allow from "${client_ip}" to any port 8443 proto tcp
if ufw status | grep -q '8443'; then
    echo "ERROR: an 8443 UFW rule remains after closeout; inspect manually before proceeding" >&2
    ufw status | grep '8443' >&2 || true
    exit 73
fi

install -o root -g fuapay-staging -m 0640 "${backup_dir}/staging.env.before" "${staging_env}"

restored_sha=$(sha256sum "${staging_env}" | awk '{print $1}')
[[ "${restored_sha}" == "${expected_baseline_sha}" ]] || {
    echo "ERROR: restored staging env hash mismatch" >&2
    exit 71
}
metadata=$(stat -c '%U:%G:%a' "${staging_env}")
[[ "${metadata}" == "root:fuapay-staging:640" ]] || {
    echo "ERROR: restored staging env metadata mismatch: ${metadata}" >&2
    exit 68
}

staging_listener=$(ss -ltnH | awk '$4 ~ /:5081$/ {print $4}' | sort -u)
[[ -z "${staging_listener}" ]] || {
    echo "ERROR: staging backend 5081 is still listening: ${staging_listener}" >&2
    exit 73
}

assert_production_unchanged

rm -f "${state_file}"
rm -rf "${backup_dir}"

echo
echo "=== FUA PAY STAGING WINDOW FOR FUA PRINT: CLOSED ==="
echo "staging service: inactive / disabled"
echo "staging backend: no listener on 5081"
echo "UFW 8443:       no rule"
echo "staging env:    exact fail-closed baseline restored"
echo "baseline SHA:   ${expected_baseline_sha}"
echo "active print reservations for source: 0"
echo "production:     unchanged / Healthy"
echo
echo "PASS: FUA Pay staging closed and Production remained unchanged."
