import urllib.request
import json

# Probe live API without auth first; then with tenant header if supported
urls = [
    "http://localhost:5232/api/suppliers/products?PageIndex=1&PageSize=50&Skus=SP000007",
    "http://localhost:5232/api/suppliers/products?PageIndex=1&PageSize=50",
]
for u in urls:
    try:
        req = urllib.request.Request(u, headers={"X-Tenant-Id": "tenant_dute-corporation_6af74f2b"})
        with urllib.request.urlopen(req, timeout=10) as r:
            body = r.read().decode("utf-8")
            print(u, "->", r.status, body[:500])
    except Exception as e:
        print(u, "-> ERR", e)
