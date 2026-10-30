"""Call the real Finance close command from disposable browser acceptance fixtures."""
import json
import urllib.request


def close_period(origin, token, tenant, company, period):
    url = f"{origin}/api/v1/tenants/{tenant}/companies/{company}/finance/periods/{period}"
    headers = {
        "Authorization": f"Bearer {token}",
        "If-Match": '"1"',
        "Idempotency-Key": f"acceptance-close-{period}",
        "Content-Type": "application/json",
    }
    request = urllib.request.Request(url + "/close", data=json.dumps({"comment": "Acceptance close"}).encode(), headers=headers, method="POST")
    with urllib.request.urlopen(request, timeout=30) as response:
        receipt = json.load(response)
    assert receipt["reference"] == period and receipt["appliedRevision"] == 2, receipt
    return receipt
