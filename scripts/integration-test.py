"""Exercise the running app with a real MySQL database. Use a disposable database.
Requires ConnectionStrings__Default; builds must already have completed.
"""
import html
import http.cookiejar
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import socketserver
import threading
from email import policy
from email.parser import BytesParser

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "src/Tdla.Web/bin/Release/net10.0/Tdla.Web.dll"
assert os.environ.get("ConnectionStrings__Default"), "Set ConnectionStrings__Default to a disposable MySQL database."
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    PORT = sock.getsockname()[1]
BASE = f"http://127.0.0.1:{PORT}"
PASSWORD = os.environ.get("TDLA_TEST_PASSWORD", "Integration-test99!")


class SmtpHandler(socketserver.StreamRequestHandler):
    def handle(self):
        self.wfile.write(b"220 localhost TDLA test SMTP\r\n")
        while line := self.rfile.readline():
            command = line.split(b" ", 1)[0].strip().upper()
            if command in (b"EHLO", b"HELO"):
                self.wfile.write(b"250-localhost\r\n250 8BITMIME\r\n")
            elif command == b"DATA":
                if self.server.reject:
                    self.wfile.write(b"451 Temporary test failure\r\n")
                    continue
                self.wfile.write(b"354 End with dot\r\n")
                data = bytearray()
                while (part := self.rfile.readline()) not in (b".\r\n", b""):
                    data.extend(part[1:] if part.startswith(b"..") else part)
                self.server.messages.append(BytesParser(policy=policy.default).parsebytes(bytes(data)))
                self.wfile.write(b"250 Accepted\r\n")
            elif command == b"QUIT":
                self.wfile.write(b"221 Bye\r\n")
                break
            else:
                self.wfile.write(b"250 OK\r\n")


SMTP = socketserver.ThreadingTCPServer(("127.0.0.1", 0), SmtpHandler)
SMTP.messages = []
SMTP.reject = False
threading.Thread(target=SMTP.serve_forever, daemon=True).start()


class Browser:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path, data=None, content_type=None):
        headers = {}
        if isinstance(data, dict):
            data = urllib.parse.urlencode(data, doseq=True).encode()
            headers["Content-Type"] = "application/x-www-form-urlencoded"
        if content_type:
            headers["Content-Type"] = content_type
        req = urllib.request.Request(BASE + path, data=data, headers=headers)
        try:
            response = self.opener.open(req, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        return response.status, response.read(), response.headers

    def get(self, path):
        status, body, _ = self.request(path)
        assert status == 200, (path, status, body[:300])
        return body.decode()

    def post(self, path, fields, page):
        token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', self.get(page))
        assert token, f"No CSRF token on {page}"
        fields = dict(fields, __RequestVerificationToken=html.unescape(token[1]))
        return self.request(path, fields)

    def login(self, username, password=PASSWORD):
        status, body, _ = self.post("/account/login", {"Username": username, "Password": password}, "/account/login")
        assert status == 200 and b"Association files" in body, body[:500]


def run():
    anon, admin, member, newcomer = Browser(), Browser(), Browser(), Browser()
    assert '"authenticated":false' in anon.get("/api/session")
    assert "Create an account" in anon.get("/account/register")
    assert anon.request("/account/register", {"Username": "bad"})[0] == 400
    assert "Member sign in" in anon.get("/admin")
    admin.login("simonwalker0@gmail.com")
    assert json.loads(admin.get("/api/session"))["admin"] is True
    for role in ["Member", "Administrator", "President", "Vice-President", "Treasurer", "Secretary"]:
        assert f'value="{role}"' in admin.get("/admin"), role
    assert 'value="Admin"' not in admin.get("/admin")
    assert admin.request("/admin/registration", {"enabled": "true"})[0] == 400
    assert admin.post("/admin/registration", {"enabled": "true"}, "/admin")[0] == 200
    assert "Create an account" in anon.get("/account/register")
    suffix = uuid.uuid4().hex[:10]
    email1, email2 = f"member-{suffix}@example.com", f"second-{suffix}@example.com"
    status, body, _ = admin.post("/admin/invite", {"emails": f"{email1}\n{email2}"}, "/admin")
    assert status == 200
    links = [html.unescape(x) for x in re.findall(r'<textarea[^>]*readonly[^>]*>(.*?)</textarea>', body.decode(), re.S)]
    assert len(links) == 2, body[:500]
    link = urllib.parse.urlsplit(links[0])
    setup_path = link.path + "?" + link.query
    fields = {k: v[0] for k, v in urllib.parse.parse_qs(link.query).items()}
    fields.update(Password=PASSWORD, ConfirmPassword=PASSWORD)
    assert member.post("/account/setup", fields, setup_path)[0] == 200
    replay = member.post("/account/setup", fields, setup_path)
    assert b"Invalid token" in replay[1], "Setup token must be single-use"
    member.login(email1)
    assert member.request("/admin")[0] == 403
    member_row = next(r for r in re.findall(r"<tr>(.*?)</tr>", admin.get("/admin"), re.S) if email1 in r)
    member_id = re.search(r'/admin/([0-9a-f]{32})/profile', member_row)[1]
    assert "awaiting verification" in member.get("/files")
    assert admin.post(f"/admin/{member_id}/profile", {"fullName": "Invited Member", "verified": "true"}, "/admin")[0] == 200
    token = html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', admin.get("/files"))[1])
    boundary = uuid.uuid4().hex
    parts = []
    description = "File overview <script>alert(1)</script>\nSecond line."
    for key, value in {"__RequestVerificationToken": token, "listed": "true", "allowedRoles": "Member", "description": description}.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n')
    filename = f"private-{suffix}.txt"
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="upload"; filename="{filename}"\r\nContent-Type: text/plain\r\n\r\nprivate contents\r\n--{boundary}--\r\n')
    oversized = "".join(parts).replace(description, "x" * 4001).encode()
    assert admin.request("/files/upload", oversized, f"multipart/form-data; boundary={boundary}")[0] == 400
    assert filename not in admin.get("/files"), "Oversized descriptions must not create files"
    status, body, _ = admin.request("/files/upload", "".join(parts).encode(), f"multipart/form-data; boundary={boundary}")
    assert status == 200, body[:500]
    file_id = re.search(r'/files/([0-9a-f]{32})/download', body.decode())[1]
    path = f"/files/{file_id}/download"
    assert anon.request(path)[0] == 404
    assert filename not in anon.get("/files")
    assert member.request(path)[1] == b"private contents"
    description_path = f"/files/{file_id}/description"
    assert "&lt;script&gt;alert(1)&lt;/script&gt;" in member.get("/files")
    assert "<script>alert(1)</script>" not in member.get("/files")
    assert admin.request(description_path, {"description": "No CSRF"})[0] == 400
    assert member.post(description_path, {"description": "Not allowed"}, "/files")[0] == 403
    updated_description = "Updated description <b>plain text</b>\nNew line."
    assert admin.post(description_path, {"description": updated_description}, "/files")[0] == 200
    assert "Updated description &lt;b&gt;plain text&lt;/b&gt;" in member.get("/files")
    assert member.request(path)[1] == b"private contents", "Editing description must preserve access and bytes"
    assert admin.post(description_path, {"description": "x" * 4001}, "/files")[0] == 400
    assert "Updated description &lt;b&gt;plain text&lt;/b&gt;" in member.get("/files")
    assert admin.post(description_path, {"description": ""}, "/files")[0] == 200
    assert "Updated description" not in member.get("/files")
    assert admin.post(description_path, {"description": updated_description}, "/files")[0] == 200
    # Public registration cannot grant itself access to member files.
    registration = {"Email": f"new-{suffix}@example.com", "Username": f"new-{suffix}", "FullName": "New Person", "EmployerName": "", "Password": PASSWORD, "ConfirmPassword": PASSWORD, "Verified": "true", "Roles": "Administrator"}
    signup = newcomer.post("/account/register", registration, "/account/register")
    assert signup[0] == 200 and b"Your account has been created" in signup[1]
    assert b"until an administrator has verified them" in signup[1]
    assert json.loads(newcomer.get("/api/session"))["authenticated"] is True
    assert json.loads(newcomer.get("/api/session"))["admin"] is False
    assert newcomer.request(path)[0] == 404
    assert filename not in newcomer.get("/files")
    admin.post(f"/files/{file_id}/permissions", {"isPublic": "true", "listed": "true"}, "/files")
    assert anon.request(path)[0] == 200, "Public files remain available to signed-out visitors"
    assert newcomer.request(path)[0] == 404, "Unverified accounts must not download public files while signed in"
    assert filename not in newcomer.get("/files")
    admin.post(f"/files/{file_id}/permissions", {"allowedRoles": "Member", "listed": "true"}, "/files")
    new_row = next(r for r in re.findall(r"<tr>(.*?)</tr>", admin.get("/admin"), re.S) if registration["Email"] in r)
    assert 'value="New Person"' in new_row
    verified_input = re.search(r'<input[^>]*name="verified"[^>]*>', new_row)[0]
    assert "checked" not in verified_input, "Registrants cannot verify themselves"
    newcomer_id = re.search(r'/admin/([0-9a-f]{32})/profile', new_row)[1]
    assert newcomer.post(f"/admin/{newcomer_id}/profile", {"verified": "true"}, "/files")[0] == 403
    assert admin.post(f"/admin/{newcomer_id}/roles", {"selectedRoles": "Member"}, "/admin")[0] == 200
    newcomer.login(registration["Username"])
    assert newcomer.request(path)[0] == 404, "A role alone must not bypass verification"
    assert admin.post(f"/admin/{newcomer_id}/profile", {"fullName": "New Person", "employerName": "Example Firm", "verified": "true"}, "/admin")[0] == 200
    assert newcomer.request(path)[1] == b"private contents", "Verification takes effect without another login"
    assert admin.post(f"/admin/{newcomer_id}/profile", {"fullName": "New Person", "employerName": "Example Firm"}, "/admin")[0] == 200
    assert newcomer.request(path)[0] == 404, "Removing verification revokes access immediately"
    assert admin.post(f"/admin/{newcomer_id}/profile", {"fullName": "New Person", "employerName": "Example Firm", "verified": "true"}, "/admin")[0] == 200
    assert "Example Firm" in newcomer.get("/account/profile")
    assert newcomer.post("/account/profile", {"FullName": "Updated Person", "EmployerName": "Updated Employer", "Verified": "false"}, "/account/profile")[0] == 200
    new_row = next(r for r in re.findall(r"<tr>(.*?)</tr>", admin.get("/admin"), re.S) if registration["Email"] in r)
    assert "Updated Employer" in new_row and "Updated Person" in new_row
    assert "checked" in re.search(r'<input[^>]*name="verified"[^>]*>', new_row)[0]
    assert member.post(f"/files/{file_id}/permissions", {"isPublic": "true"}, "/files")[0] == 403
    admin.post(f"/files/{file_id}/permissions", {"isPublic": "true"}, "/files")
    assert anon.request(path)[1] == b"private contents"
    assert filename not in anon.get("/files"), "Hidden public files must not be listed"
    admin.post(f"/files/{file_id}/permissions", {"isPublic": "true", "listed": "true"}, "/files")
    assert filename in anon.get("/files")
    admin.post(f"/files/{file_id}/permissions", {"allowedRoles": "Member", "listed": "true"}, "/files")
    assert anon.request(path)[0] == 404
    admin_page = admin.get("/admin")
    row = next(r for r in re.findall(r"<tr>(.*?)</tr>", admin_page, re.S) if email1 in r)
    user_id = re.search(r'/admin/([0-9a-f]{32})/roles', row)[1]
    assert admin.post(f"/admin/{user_id}/roles", {}, "/admin")[0] == 200
    assert member.request(path)[0] == 404, "Role removal must invalidate existing sessions immediately"
    for blocked in ["/App_Data/tdla.db", "/appsettings.json", "/php/info.php", "/.git/config", "/uploads/" + file_id]:
        assert anon.request(blocked)[0] == 404, blocked
    admin.post(f"/files/{file_id}/delete", {}, "/files")
    assert admin.request(path)[0] == 404
    admin.post("/admin/registration", {}, "/admin")
    assert "registration is currently closed" in anon.get("/account/register")
    assert anon.post("/account/register", registration, "/account/login")[0] == 403
    root = anon.get("/")
    assert root.encode() == (ROOT / "index.html").read_bytes(), "Home page must be served unchanged"
    assert anon.request("/js/navbar-session.js")[0] == 200
    assert "no-store" in anon.request("/api/session")[2].get("Cache-Control", "")
    # Recovery sends only to the stored address and never exposes whether an account exists.
    email_address = registration["Email"]
    before = len(SMTP.messages)
    status, known_response, _ = anon.post("/account/forgot-username", {"Email": email_address}, "/account/forgot-username")
    assert status == 200 and b"Check your email" in known_response
    assert len(SMTP.messages) == before + 1
    reminder = SMTP.messages[-1]
    assert reminder["To"] == email_address and registration["Username"] in reminder.get_content()
    assert "no-reply@tdla.com.au" in reminder["From"]
    unknown = anon.post("/account/forgot-username", {"Email": f"absent-{suffix}@example.com"}, "/account/forgot-username")
    assert unknown[1] == known_response and len(SMTP.messages) == before + 1
    assert anon.request("/account/forgot-password", {"Email": email_address})[0] == 400
    assert anon.post("/account/forgot-password", {"Email": email_address}, "/account/forgot-password")[0] == 200
    reset_message = SMTP.messages[-1]
    assert reset_message["To"] == email_address and reset_message["Subject"] == "Reset your TDLA password"
    reset_url = re.search(r'http://[^\s]+/account/setup\?[^\s]+', reset_message.get_content())[0]
    reset = urllib.parse.urlsplit(reset_url)
    reset_fields = {k: v[0] for k, v in urllib.parse.parse_qs(reset.query).items()}
    reset_page = reset.path + "?" + reset.query
    new_password = "Changed-test99!"
    reset_fields.update(Password=new_password, ConfirmPassword=new_password)
    tampered = dict(reset_fields, token=reset_fields["token"] + "invalid")
    assert b"Invalid token" in anon.post("/account/setup", tampered, reset_page)[1]
    assert anon.post("/account/setup", reset_fields, reset_page)[0] == 200
    assert SMTP.messages[-1]["Subject"] == "TDLA password changed"
    assert json.loads(newcomer.get("/api/session"))["authenticated"] is False
    assert b"Invalid token" in anon.post("/account/setup", reset_fields, reset_page)[1]
    newcomer.login(registration["Username"], new_password)
    assert newcomer.request("/account/logout", {})[0] == 400
    session = json.loads(newcomer.get("/api/session"))
    assert session["logoutToken"]
    assert newcomer.request("/account/logout", {"__RequestVerificationToken": session["logoutToken"]})[0] == 200
    assert json.loads(newcomer.get("/api/session"))["authenticated"] is False
    # A failed mail server must not reveal account existence or change any password.
    SMTP.reject = True
    failure = anon.post("/account/forgot-password", {"Email": email_address}, "/account/forgot-password")
    SMTP.reject = False
    assert failure[0] == 200 and failure[1] == known_response
    admin.post("/admin/registration", {"enabled": "true"}, "/admin")
    print("PASS: signup notice, verification-gated file access, administrator-only descriptions (upload/edit/clear/limits/HTML escaping), all six roles, email recovery, session invalidation, CSRF and unchanged homepage.")


with tempfile.TemporaryDirectory(prefix="tdla-integration-") as tmp:
    php_config = Path(tmp) / "info.php"
    php_config.write_text("<?php\nconst SMTP_HOST = '127.0.0.1';\nconst SMTP_PORT = " + str(SMTP.server_address[1]) + ";\nconst SMTP_USER = '';\nconst SMTP_PASS = '';\nconst SMTP_SECURE = '';\nconst MAIL_FROM = 'no-reply@tdla.com.au';\nconst MAIL_FROM_NAME = 'TDLA Website Contact Form';\n", encoding="utf-8")
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=BASE,
               Storage__Path=tmp, Site__PublicUrl=BASE, Bootstrap__AdminPassword=PASSWORD,
               Smtp__LegacyConfigPath=str(php_config),
               Logging__LogLevel__Default="Warning")
    env.pop("TDLA_CONFIG_FILE", None)
    with open(Path(tmp) / "server.log", "w+", encoding="utf-8") as log:
        process = subprocess.Popen(["dotnet", str(DLL)], cwd=ROOT / "src/Tdla.Web", env=env, stdout=log, stderr=log)
        try:
            for attempt in range(120):
                if process.poll() is not None:
                    log.seek(0)
                    raise RuntimeError(log.read())
                try:
                    if Browser().request("/health")[0] == 200:
                        break
                except (OSError, urllib.error.URLError):
                    time.sleep(0.5)
            else:
                raise RuntimeError("Application did not start")
            run()
        except Exception:
            log.seek(0)
            print(log.read()[-12000:])
            raise
        finally:
            process.terminate()
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
            SMTP.shutdown()
            SMTP.server_close()
