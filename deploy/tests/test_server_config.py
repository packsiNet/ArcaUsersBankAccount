import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('config', Path(__file__).parents[1] / 'server_config.py')
config = importlib.util.module_from_spec(spec)
spec.loader.exec_module(config)


class ConfigurationTests(unittest.TestCase):
    def settings(self, **overrides):
        return dict(app_url='https://account.zanatech.online', release='a'*40+'-123-1', **overrides)

    def test_redeploy_preserves_every_secret(self):
        with tempfile.TemporaryDirectory() as directory:
            config.prepare(self.settings(), directory)
            first = (Path(directory)/'secrets.json').read_bytes()
            config.prepare(self.settings(), directory)
            self.assertEqual(first, (Path(directory)/'secrets.json').read_bytes())
            state = json.loads(first)
            runtime = (Path(directory)/'account.env').read_text()
            self.assertIn(state['runtime_password'], runtime)
            self.assertNotIn(state['owner_password'], runtime)
            self.assertNotIn(state['admin_password'], runtime)

    def test_rejects_unsafe_urls(self):
        for url in ['http://example.com', 'https://example.com:443', 'https://user@example.com',
                    'https://example.com/path', 'https://example.com/?x=1', 'https://example.com/#x',
                    'https://a;touch.example.com', 'https://127.0.0.1']:
            settings = self.settings(); settings['app_url'] = url
            with self.subTest(url=url), self.assertRaises(ValueError): config.validate(settings)

    def test_api_token_is_private_preserved_and_rotatable(self):
        with tempfile.TemporaryDirectory() as directory:
            result = config.prepare(self.settings(api_ir_token='Bearer synthetic-token-1'), directory)
            root = Path(directory)
            self.assertNotIn('api_ir_token', result)
            self.assertNotIn('synthetic-token-1', (root/'deployment.json').read_text())
            self.assertNotIn('synthetic-token-1', (root/'migration.env').read_text())
            self.assertIn('ApiIr__Token=synthetic-token-1\n', (root/'account.env').read_text())
            first = json.loads((root/'secrets.json').read_text())
            config.prepare(self.settings(api_ir_token=''), directory)
            self.assertEqual(first, json.loads((root/'secrets.json').read_text()))
            self.assertIn('ApiIr__Token=synthetic-token-1\n', (root/'account.env').read_text())
            config.prepare(self.settings(api_ir_token='synthetic-token-2'), directory)
            second = json.loads((root/'secrets.json').read_text())
            self.assertEqual({k:v for k,v in first.items() if k != 'api_ir_token'},
                             {k:v for k,v in second.items() if k != 'api_ir_token'})
            self.assertIn('ApiIr__Token=synthetic-token-2\n', (root/'account.env').read_text())

    def test_preserves_manually_configured_api_token(self):
        with tempfile.TemporaryDirectory() as directory:
            config.prepare(self.settings(), directory)
            runtime = Path(directory)/'account.env'
            runtime.write_text(runtime.read_text() + 'ApiIr__Token="Bearer synthetic-manual-token"\n')
            config.prepare(self.settings(), directory)
            self.assertIn('ApiIr__Token=synthetic-manual-token\n', runtime.read_text())

    def test_rejects_api_token_environment_injection(self):
        for token in ['token\nInjected=value', 'token"', '$(whoami)', 'token\\']:
            with tempfile.TemporaryDirectory() as directory:
                with self.subTest(token=token), self.assertRaises(ValueError):
                    config.prepare(self.settings(api_ir_token=token), directory)
                self.assertFalse((Path(directory)/'secrets.json').exists())

    def test_rejects_shell_injection(self):
        settings = self.settings(); settings['release'] = '$(whoami)'
        with self.assertRaises(ValueError): config.validate(settings)
        with self.assertRaises(ValueError): config.validate(self.settings(admin_email='a@b.com\nX=secret'))

    def test_refuses_unmanaged_existing_installation(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory)/'account.env').write_text('existing key')
            with self.assertRaises(ValueError): config.prepare(self.settings(), directory)
            self.assertFalse((Path(directory)/'secrets.json').exists())

    def test_refuses_domain_change(self):
        with tempfile.TemporaryDirectory() as directory:
            config.prepare(self.settings(), directory)
            settings = self.settings(); settings['app_url'] = 'https://other.example.com'
            with self.assertRaises(ValueError): config.prepare(settings, directory)


if __name__ == '__main__': unittest.main()
