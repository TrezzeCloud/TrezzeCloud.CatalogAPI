#!/usr/bin/env python3
"""HTTPS loopback proxy to an insecure Elasticsearch test container.

For GitHub Actions only. Never publish port 9443 to a public interface.
The API implementation itself always uses HTTPS with an API key.
"""
import argparse
import http.client
import http.server
import ssl


class Proxy(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        self.forward()

    def do_PUT(self):
        self.forward()

    def do_POST(self):
        self.forward()

    def forward(self):
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length) if length else None
        upstream = http.client.HTTPConnection("127.0.0.1", 9200, timeout=25)
        try:
            headers = {}
            if self.headers.get("Content-Type"):
                headers["Content-Type"] = self.headers["Content-Type"]
            upstream.request(self.command, self.path, body=body, headers=headers)
            result = upstream.getresponse()
            response_data = result.read()
            self.send_response(result.status)
            if result.getheader("Content-Type"):
                self.send_header("Content-Type", result.getheader("Content-Type"))
            self.send_header("Content-Length", str(len(response_data)))
            self.end_headers()
            self.wfile.write(response_data)
        finally:
            upstream.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cert", required=True)
    parser.add_argument("--key", required=True)
    args = parser.parse_args()

    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(certfile=args.cert, keyfile=args.key)
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 9443), Proxy)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
