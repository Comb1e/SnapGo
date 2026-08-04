#!/bin/bash
# SnapGo OCR Pipeline Diagnostic Script
# Tests each layer independently to isolate failures
# Usage: bash test/test_ocr.sh [test-image.png]

# Use $PYTHON if available, otherwise python
PYTHON=$(command -v python3 || command -v python || echo "python")

BASE_URL="http://localhost:8000"
PASS=0
FAIL=0

pass() { echo "  PASS: $1"; ((PASS++)); }
fail() { echo "  FAIL: $1"; ((FAIL++)); }
info() { echo "  INFO: $1"; }

# ============================================================
# Layer 1: Docker Container
# ============================================================
echo ""
echo "=== Layer 1: Docker Container Status ==="
STATUS=$(docker ps --filter name=snapgo-ocr --format "{{.Status}}" 2>/dev/null)
if [ -z "$STATUS" ]; then
    fail "Container 'snapgo-ocr' not found or not running"
    echo "  Run: cd docker && docker compose up -d"
    exit 1
fi
if echo "$STATUS" | grep -q "^Up"; then
    pass "Container running: $STATUS"
else
    fail "Container status: $STATUS"
fi

# ============================================================
# Layer 2: Health Endpoint
# ============================================================
echo ""
echo "=== Layer 2: Health Endpoint ==="
HEALTH=$(curl -s --max-time 5 "$BASE_URL/health" 2>&1)
if echo "$HEALTH" | "$PYTHON" -c "import sys,json; d=json.load(sys.stdin); sys.exit(0 if d.get('errorCode')==0 else 1)" 2>/dev/null; then
    pass "Health check OK: $HEALTH"
else
    fail "Health endpoint failed: $HEALTH"
    exit 1
fi

# ============================================================
# Layer 3: Container Error Check
# ============================================================
echo ""
echo "=== Layer 3: Container Error Check ==="
LOGS=$(docker logs snapgo-ocr --tail 30 2>&1)
if echo "$LOGS" | grep -q "Connection error"; then
    fail "VLM worker connection error detected"
    info "The pipeline cannot reach its vLLM model server backend."
elif echo "$LOGS" | grep -q "RuntimeError"; then
    fail "Runtime errors in container logs"
    echo "$LOGS" | grep -E "Error|Exception" | tail -5
else
    pass "No runtime errors in recent container logs"
fi

# ============================================================
# Layer 4: Pipeline Config
# ============================================================
echo ""
echo "=== Layer 4: Pipeline Configuration ==="
# Check both possible config locations
for CFG_PATH in /home/paddleocr/pipeline_config_native.yaml /home/paddleocr/pipeline_config_vllm.yaml; do
    CFG=$(docker exec snapgo-ocr sh -c "cat $CFG_PATH" 2>&1) || true
    [ -n "$CFG" ] && break
done
BACKEND=$(echo "$CFG" | grep "backend:" | head -1 | awk '{print $2}')
SERVER_URL=$(echo "$CFG" | grep "server_url:" | head -1 | awk '{print $2}')
info "VLRecognition backend: $BACKEND"
if [ -n "$SERVER_URL" ]; then
    info "Server URL: $SERVER_URL"
fi

if [ "$BACKEND" = "native" ]; then
    pass "Using LOCAL (native) inference — no external server needed"
elif [ "$BACKEND" = "vllm-server" ]; then
    fail "Using vllm-server backend — requires separate vLLM container"

    # Check if vllm server container exists
    if docker ps --filter name=paddleocr-vlm-server --format "{{.Status}}" 2>/dev/null | grep -q "^Up"; then
        pass "vLLM server container is running"
    else
        fail "vLLM server container 'paddleocr-vlm-server' is NOT running"
        info "The pipeline orchestrator sends OCR requests to $SERVER_URL"
        info "But there's no vLLM server container to receive them."
    fi
else
    info "Unknown backend: $BACKEND"
fi

# ============================================================
# Layer 5: OCR Endpoint (real test)
# ============================================================
echo ""
echo "=== Layer 5: OCR Pipeline Test ==="

if [ -n "$1" ] && [ -f "$1" ]; then
    info "Using image: $1"
    B64=$(base64 -w0 "$1")
else
    info "No test image provided. Create a test PNG with Python..."
    # Generate a small test PNG using $PYTHON (available in Git Bash)
    B64=$($PYTHON -c "
import base64, struct, zlib
w, h = 300, 100
def chunk(t, d):
    c = t + d
    return struct.pack('>I', len(d)) + c + struct.pack('>I', zlib.crc32(c) & 0xffffffff)
# White image
raw = b''
for y in range(h):
    raw += b'\x00' + b'\xff\xff\xff' * w
ihdr = chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
idat = chunk(b'IDAT', zlib.compress(raw))
iend = chunk(b'IEND', b'')
png = b'\x89PNG\r\n\x1a\n' + ihdr + idat + iend
print(base64.b64encode(png).decode())
")
fi

RESPONSE=$(curl -s --max-time 60 -X POST "$BASE_URL/layout-parsing" \
    -H "Content-Type: application/json" \
    -d "{\"file\":\"$B64\",\"fileType\":1}" 2>&1)

HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 60 -X POST "$BASE_URL/layout-parsing" \
    -H "Content-Type: application/json" \
    -d "{\"file\":\"$B64\",\"fileType\":1}" 2>&1)

echo "  HTTP status: $HTTP_CODE"

ECODE=$(echo "$RESPONSE" | $PYTHON -c "import sys,json; d=json.load(sys.stdin); print(d.get('errorCode','N/A'))" 2>/dev/null || echo "PARSE_ERROR")

if [ "$HTTP_CODE" = "200" ] && [ "$ECODE" = "0" ]; then
    RESULTS=$(echo "$RESPONSE" | $PYTHON -c "import sys,json; d=json.load(sys.stdin); print(len(d.get('result',{}).get('layoutParsingResults',[])))" 2>/dev/null)
    TEXT=$(echo "$RESPONSE" | $PYTHON -c "
import sys,json
d=json.load(sys.stdin)
texts=[]
for r in d.get('result',{}).get('layoutParsingResults',[]):
    t=r.get('markdown',{}).get('text','')
    if t: texts.append(t[:100])
print(' | '.join(texts) if texts else '(empty)')
" 2>/dev/null)
    pass "OCR pipeline OK — $RESULTS result(s): $TEXT"
elif [ "$HTTP_CODE" = "500" ]; then
    fail "OCR returned HTTP 500 — VLM model server unreachable"
    info "Root cause: Missing vLLM model server container"
    info "The pipeline tries to connect to: $SERVER_URL"
elif [ "$ECODE" != "N/A" ]; then
    fail "OCR returned errorCode=$ECODE"
    echo "$RESPONSE" | $PYTHON -m json.tool 2>/dev/null | head -10
else
    fail "HTTP $HTTP_CODE — raw: $(echo "$RESPONSE" | head -c 200)"
fi

# ============================================================
# Summary
# ============================================================
echo ""
echo "========================================"
echo "  SUMMARY: $PASS passed, $FAIL failed"
echo "========================================"
if [ "$FAIL" -gt 0 ]; then
    echo ""
    echo "Root cause analysis:"
    echo "  Check the failed layers above and inspect:"
    echo "    docker logs snapgo-ocr --tail 50"
    echo "    docker exec snapgo-ocr sh -c 'cat /home/paddleocr/pipeline_config_native.yaml'"
fi
