#!/bin/sh
# macOS/Linux용 tracker 설치. 최초 1회만 실행한다.
set -e
cd "$(dirname "$0")"

# Intel Mac 은 mediapipe 0.10.21 을 쓰는데 wheel 이 cp312 까지만 있다
# (근거: PythonTracker/requirements-intel-mac.txt 주석). 3.12 이하를 우선 고른다.
PY=""
for c in python3.12 python3.11 python3.10 python3; do
    if command -v "$c" >/dev/null 2>&1; then
        PY="$c"
        break
    fi
done

if [ -z "$PY" ]; then
    echo "Python not found. Install Python 3.12 first: https://www.python.org/downloads/"
    exit 1
fi

echo "[1/2] Creating virtual environment with $PY ($($PY --version 2>&1))..."
"$PY" -m venv .venv

echo "[2/2] Installing mediapipe + opencv (takes a few minutes)..."
.venv/bin/python -m pip install --upgrade pip
if ! .venv/bin/python -m pip install -r requirements.txt; then
    echo ""
    echo "Install failed. On Intel Mac this usually means the Python version is too new."
    echo "mediapipe 0.10.21 needs Python 3.12 or older."
    exit 1
fi

echo ""
echo "Setup complete. Now run: ./run_tracker.sh"
