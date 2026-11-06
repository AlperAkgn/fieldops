"""Exercise browser-style authentication against a disposable local Compose stack."""

import argparse
import json
import re
import secrets
import ssl
import time
from http.client import HTTPSConnection
from http.cookiejar import CookieJar
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPCookieProcessor, HTTPSHandler, Request, build_opener, urlopen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ca", required=True, help="Public localhost development certificate in PEM format")
    parser.add_argument("--api", default="https://localhost:5443")
    parser.add_argument("--http", default="http://localhost:5080")
    parser.add_argument("--mail", default="http://localhost:8025")
    args = parser.parse_args()
    origin = "https://localhost:5173"
    cookies = CookieJar()
    tls = ssl.create_default_context(cafile=args.ca)
    browser = build_opener(HTTPCookieProcessor(cookies), HTTPSHandler(context=tls))

    def request(method, path, status, *, body=None, csrf=None, access=None, extra=None, base=None):
        headers = {"Accept": "application/json", "Origin": origin}
        if body is not None:
            headers["Content-Type"] = "application/json"
        if csrf is not None:
            headers["X-CSRF-TOKEN"] = csrf
        if access is not None:
            headers["Authorization"] = "Bearer " + access
        headers.update(extra or {})
        payload = json.dumps(body).encode() if body is not None else None
        http_request = Request((base or args.api) + path, payload, headers, method=method)
        try:
            with browser.open(http_request, timeout=10) as response:
                code, response_headers, content = response.status, response.headers, response.read()
        except HTTPError as error:
            code, response_headers, content = error.code, error.headers, error.read()
        assert code == status, f"{method} {path} expected {status}, received {code}"
        return (json.loads(content) if content else None), response_headers

    def mail_json(path):
        with urlopen(args.mail + path, timeout=5) as response:
            return json.load(response)

    def confirmation_for(email):
        for _ in range(30):
            for item in mail_json("/api/v1/messages")["messages"]:
                if (item["Subject"] == "Confirm your FieldOps email"
                        and any(recipient["Address"] == email for recipient in item["To"])):
                    message = mail_json("/api/v1/message/" + item["ID"])["Text"]
                    user = re.search(r"^User ID:\s*(\S+)", message, re.MULTILINE)
                    token = re.search(r"^Token:\s*(\S+)", message, re.MULTILINE)
                    assert user and token, "Confirmation email has no action credential"
                    return user.group(1), token.group(1)
            time.sleep(0.5)
        raise AssertionError("No registration message arrived in local Mailpit")

    web = "/api/v1/auth/web"
    auth = "/api/v1/auth"
    refresh_name = "__Secure-fieldops-refresh"
    for probe in ("live", "ready"):
        body, _ = request("GET", "/health/" + probe, 200)
        assert body["status"] == "Healthy"
    problem, headers = request("GET", web + "/csrf", 400, base=args.http)
    assert problem["code"] == "https_required" and not headers.get("Set-Cookie")
    _, preflight = request("OPTIONS", web + "/register", 204, extra={
        "Access-Control-Request-Method": "POST",
        "Access-Control-Request-Headers": "content-type,x-csrf-token",
    })
    assert preflight.get("Access-Control-Allow-Origin") == origin
    assert preflight.get("Access-Control-Allow-Credentials") == "true"

    csrf_response, headers = request("GET", web + "/csrf", 200)
    csrf = csrf_response["token"]
    csrf_cookie = headers.get("Set-Cookie", "").lower()
    assert all(part in csrf_cookie for part in ("__host-fieldops-csrf", "secure", "httponly", "samesite=strict"))

    email = f"tls-smoke-{secrets.token_hex(8)}@fieldops.test"
    password = "Smoke!Phase1-" + secrets.token_hex(16)
    registration = {"email": email, "displayName": "TLS Smoke", "organizationName": "TLS Smoke Workspace"}
    request("POST", web + "/register", 403, body=registration)
    accepted, headers = request("POST", web + "/register", 202, body=registration, csrf=csrf)
    assert set(accepted) == {"message"} and not headers.get("Set-Cookie") and not headers.get("Location")
    duplicate, _ = request("POST", web + "/register", 202, body={**registration, "email": email.upper()}, csrf=csrf)
    assert accepted == duplicate
    credentials = {"email": email, "password": password}
    request("POST", web + "/login", 401, body=credentials, csrf=csrf)
    user, token = confirmation_for(email)
    request("POST", auth + "/confirm-email", 204, body={"userId": user, "token": token, "password": password})
    request("POST", web + "/login", 403, body=credentials)
    login, headers = request("POST", web + "/login", 200, body=credentials, csrf=csrf)
    refresh_cookie = headers.get("Set-Cookie", "").lower()
    assert all(part in refresh_cookie for part in (refresh_name.lower(), "secure", "httponly", "samesite=strict"))
    assert "refreshToken" not in login
    first_refresh = next(cookie.value for cookie in cookies if cookie.name == refresh_name)

    refreshed, _ = request("POST", web + "/refresh", 200, csrf=csrf)
    replacement = next(cookie.value for cookie in cookies if cookie.name == refresh_name)
    assert first_refresh != replacement and "refreshToken" not in refreshed
    access = refreshed["accessToken"]
    auth_csrf, _ = request("GET", web + "/csrf", 200, access=access)
    request("POST", web + "/logout", 403, access=access)
    request("POST", web + "/logout", 204, csrf=auth_csrf["token"], access=access)
    assert not any(cookie.name == refresh_name for cookie in cookies)
    request("GET", auth + "/me", 401, access=access)
    request("POST", web + "/refresh", 401, csrf=csrf,
            extra={"Cookie": f"{refresh_name}={replacement}; " + "; ".join(
                f"{cookie.name}={cookie.value}" for cookie in cookies)})
    new_login, _ = request("POST", web + "/login", 200, body=credentials, csrf=csrf)
    api = urlsplit(args.api)
    connection = HTTPSConnection(api.hostname, api.port, context=tls, timeout=10)
    try:
        for _ in range(40):
            connection.request("GET", auth + "/me", headers={"Authorization": "Bearer " + new_login["accessToken"]})
            response = connection.getresponse()
            body = json.loads(response.read())
            if response.status == 429:
                assert body["code"] == "rate_limit_exceeded"
                assert 0 < int(response.getheader("Retry-After")) <= 60
                break
            assert response.status == 200
        else:
            raise AssertionError("Auth IP limit did not return HTTP 429")
    finally:
        connection.close()
    print("Trusted HTTPS, CORS, CSRF, registration, SMTP confirmation, login, refresh, logout and rate limiting passed.")


if __name__ == "__main__":
    main()
