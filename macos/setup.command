#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
export PYTHONDONTWRITEBYTECODE=1
xcrun --find swiftc >/dev/null
[[ -x runtime/bin/python3 ]] || { echo 'Missing bundled Python runtime'; exit 1; }
runtime/bin/python3 -B -m unittest discover -s tests -v
bash build.command
