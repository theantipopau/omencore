#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ./qa/collect-linux-triage.sh [output_dir] [bin_dir]
# Example:
#   ./qa/collect-linux-triage.sh ./triage-8e41 ./

OUT_DIR="${1:-./triage-output-$(date -u +%Y%m%d-%H%M%S)}"
BIN_DIR="${2:-.}"
CLI_PATH="${BIN_DIR%/}/omencore-cli"

mkdir -p "$OUT_DIR"
REPORT_PATH="$OUT_DIR/omencore-linux-triage.txt"

run_cmd() {
    local label="$1"
    shift
    echo
    echo "## ${label}"
    echo "$ $*"
    "$@"
}

run_cmd_allow_fail() {
    local label="$1"
    shift
    echo
    echo "## ${label}"
    echo "$ $*"
    "$@" || true
}

{
    echo "OmenCore Linux Triage Bundle"
    echo "Generated (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "Output directory: $OUT_DIR"
    echo "CLI path: $CLI_PATH"

    run_cmd "Kernel" uname -a
    run_cmd "OS release" cat /etc/os-release

    if [[ -x "$CLI_PATH" ]]; then
        run_cmd "CLI version" "$CLI_PATH" --version
        run_cmd_allow_fail "CLI status" sudo "$CLI_PATH" status
        run_cmd_allow_fail "CLI diagnose" sudo "$CLI_PATH" diagnose
        run_cmd_allow_fail "CLI diagnose report" sudo "$CLI_PATH" diagnose --report
    else
        echo
        echo "## CLI checks"
        echo "CLI binary not found or not executable at: $CLI_PATH"
        echo "Provide bin_dir as second argument if needed."
    fi

    run_cmd_allow_fail "hp-wmi sysfs listing" ls -la /sys/devices/platform/hp-wmi/
    run_cmd_allow_fail "hp-wmi hwmon listing" bash -lc 'ls -la /sys/devices/platform/hp-wmi/hwmon/*/'
    run_cmd_allow_fail "DMI board name" cat /sys/class/dmi/id/board_name
    run_cmd_allow_fail "DMI product name" cat /sys/class/dmi/id/product_name
    run_cmd_allow_fail "dmesg hp-wmi excerpt" bash -lc 'dmesg | grep -i "hp-wmi\|omen" | tail -n 200'

    if command -v acpidump >/dev/null 2>&1; then
        echo
        echo "## ACPI dump"
        echo "$ sudo acpidump > $OUT_DIR/acpidump.dat"
        sudo acpidump > "$OUT_DIR/acpidump.dat" || true
    else
        echo
        echo "## ACPI dump"
        echo "acpidump not found. Install acpica-tools if needed."
    fi
} | tee "$REPORT_PATH"

echo
echo "Triage collection complete."
echo "Report: $REPORT_PATH"
