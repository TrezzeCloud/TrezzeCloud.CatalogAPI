#!/usr/bin/env python3
"""Reject committed credentials in ASP.NET Core appsettings files.

Only blank values for credentials and connection strings are permitted in
tracked configuration; supply actual values through User Secrets (local) or
Kubernetes Secrets (AKS).
"""
import json
from pathlib import Path

SENSITIVE_KEYS = {
    "password",
    "passwd",
    "secret",
    "secretkey",
    "apikey",
    "accesskey",
    "connectionstring",
    "usersdatabase",
    "catalogdatabase",
}
SENSITIVE_PARENTS = {"connectionstrings"}


def violations(value, path=(), filename=""):
    if isinstance(value, dict):
        for key, item in value.items():
            yield from violations(item, (*path, str(key)), filename)
    elif isinstance(value, list):
        for item in value:
            yield from violations(item, path, filename)
    elif path:
        key = path[-1].lower()
        if (key in SENSITIVE_KEYS or any(p.lower() in SENSITIVE_PARENTS for p in path[:-1])):
            if value not in ("", None):
                yield f"{filename}: {':'.join(path)} tem valor fixo versionado"


def main():
    config_files = sorted(Path("src").rglob("appsettings*.json"))
    if not config_files:
        raise SystemExit("Nenhum arquivo appsettings*.json encontrado.")
    problems = []
    for filename in config_files:
        try:
            config = json.loads(filename.read_text(encoding="utf-8-sig"))
        except (json.JSONDecodeError, UnicodeError) as exc:
            problems.append(f"{filename}: arquivo JSON invalido ({exc})")
            continue
        problems.extend(violations(config, filename=str(filename)))

    if problems:
        print("\n".join(problems))
        raise SystemExit("ERRO: remova credenciais dos appsettings e use Secrets.")

    print(f"OK: {len(config_files)} appsettings sem credenciais fixas.")


if __name__ == "__main__":
    main()
