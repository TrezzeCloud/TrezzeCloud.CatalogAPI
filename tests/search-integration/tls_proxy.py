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

    def read_body(self):
        # HttpClient may use Transfer-Encoding: chunked for JSON requests.
        # http.server does not decode chunked request bodies automatically.
        transfer_encoding = self.headers.get("Transfer-Encoding", "").lower()
        if transfer_encoding:
            if transfer_encoding != "chunked":
                raise ValueError("Unsupported request transfer encoding")
            chunks = []
            total = 0
            while True:
                chunk_header = self.rfile.readline(128)
                size = int(chunk_header.split(b";", 1)[0].strip(), 16)
                if size == 0:
                    # Consume optional trailers through the terminating empty line.
                    while self.rfile.readline(8192) not in (b"\r\n", b"\n"):
                        pass
                    break
                total += size
                if total > 5 * 1024 * 1024:
                    raise ValueError("Request too large for test proxy")
                chunks.append(self.rfile.read(size))
                if self.rfile.read(2) != b"\r\n":
                    raise ValueError("Invalid chunk delimiter")
            return b"".join(chunks)

        length = int(self.headers.get("Content-Length", "0"))
        if length > 5 * 1024 * 1024:
            raise ValueError("Request too large for test proxy")
        return self.rfile.read(length) if length else None

    def forward(self):
        try:
            body = self.read_body()
        except (ValueError, EOFError):
            self.send_error(400, "Invalid test request body")
            self.close_connection = True
            return
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
