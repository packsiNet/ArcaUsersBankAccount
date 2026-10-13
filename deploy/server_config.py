#!/usr/bin/env python3
"""Validate deployment input and preserve server-generated secrets. Never print secrets."""
import base64
import json
import os
from pathlib import Path
import re
import secrets
import sys
from urllib.parse import urlsplit


def validate(config):
    url = urlsplit(config['app_url'])
    domain = url.hostname or ''
    if (url.scheme != 'https' or url.port is not None or url.username or url.password
            or url.path not in ('', '/') or url.query or url.fragment
            or not re.fullmatch(r'(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}', domain)):
        raise ValueError('APP_URL must be an HTTPS domain without a port, path or credentials.')
    release = config['release']
    if not re.fullmatch(r'[a-f0-9]{40}-[0-9]+-[0-9]+', release):
        raise ValueError('Invalid release identifier.')
    email = config.get('admin_email') or 'admin@' + domain
    contact = config.get('certificate_email', '')
    for value in (email, contact):
        if value and not re.fullmatch(r'[A-Za-z0-9._+%-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,63}', value):
            raise ValueError('Invalid email.')
    port = str(config.get('ssh_port') or '22')
    if not port.isdigit() or not 1 <= int(port) <= 65535:
        raise ValueError('Invalid SSH port.')
    return dict(domain=domain, release=release, admin_email=email, certificate_email=contact, ssh_port=port)


def private_write(path, data):
    path = Path(path)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    os.fchmod(fd, 0o600) if hasattr(os, 'fchmod') else None
    with os.fdopen(fd, 'w', encoding='utf-8', newline='\n') as stream:
        stream.write(data)


def api_token(value):
    token = (value or '').strip()
    if token.lower().startswith('bearer '):
        token = token[7:].strip()
    if token and not re.fullmatch(r'[A-Za-z0-9._~+/=-]+', token):
        raise ValueError('Invalid APIIR_TOKEN format.')
    return token


def prepare(config, directory):
    token = api_token(config.get('api_ir_token'))
    config = validate(config)
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    state_path = directory / 'secrets.json'
    if state_path.exists():
        state = json.loads(state_path.read_text())
        if state['domain'] != config['domain'] or state['admin_email'] != config['admin_email']:
            raise ValueError('Changing the managed domain or administrator requires a manual migration.')
        for field in ('owner_password', 'runtime_password', 'encryption_key', 'backup_key', 'admin_password'):
            if not state.get(field):
                raise ValueError('Incomplete server state; refusing to regenerate existing secrets.')
    else:
        if (directory / 'account.env').exists():
            raise ValueError('Unmanaged installation detected; import existing keys before deployment.')
        state = dict(domain=config['domain'], admin_email=config['admin_email'],
                     owner_password=secrets.token_hex(32), runtime_password=secrets.token_hex(32),
                     encryption_key=base64.b64encode(secrets.token_bytes(32)).decode(),
                     backup_key=secrets.token_hex(32), admin_password=secrets.token_urlsafe(32) + 'aA1!')
        private_write(state_path, json.dumps(state, indent=2) + '\n')
    if not token:
        token = api_token(state.get('api_ir_token'))
    # Preserve a token previously configured directly in the managed environment.
    if not token and (directory / 'account.env').exists():
        for line in (directory / 'account.env').read_text().splitlines():
            if line.startswith(('ApiIr__Token=', 'APIIR_TOKEN=')):
                token = api_token(line.partition('=')[2].strip().strip('"').strip("'"))
                if token:
                    break
    if token and state.get('api_ir_token') != token:
        state['api_ir_token'] = token
        private_write(state_path, json.dumps(state, indent=2) + '\n')
    common = 'ASPNETCORE_ENVIRONMENT=Production\nASPNETCORE_URLS=http://127.0.0.1:5080\n'
    common += f'AllowedHosts={config["domain"]}\nDataProtection__KeyPath=/var/lib/safetunnels/keys\n'
    common += f'Administration__LoginEmail={state["admin_email"]}\n'
    common += f'Encryption__ActiveKeyId=server1\nEncryption__Keys__server1={state["encryption_key"]}\n'
    def connection(user, password):
        return f'ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=safetunnels;Username={user};Password={password};Maximum Pool Size=80;Include Error Detail=false;Log Parameters=false"\n'
    runtime = common + connection('safetunnels_runtime', state['runtime_password'])
    if token:
        runtime += f'ApiIr__Token={token}\n'
    private_write(directory / 'account.env', runtime)
    private_write(directory / 'migration.env', common + connection('safetunnels_owner', state['owner_password']) +
                  f'Bootstrap__Email={state["admin_email"]}\nBootstrap__Password={state["admin_password"]}\n')
    private_write(directory / 'backup.key', state['backup_key'] + '\n')
    private_write(directory / 'deployment.json', json.dumps(config) + '\n')
    return config


if __name__ == '__main__':
    config = prepare(json.loads(Path(sys.argv[1]).read_text()), sys.argv[2])
    # Only validated, non-secret shell assignments are emitted.
    for key, value in config.items():
        print(f"{key.upper()}='{value}'")
