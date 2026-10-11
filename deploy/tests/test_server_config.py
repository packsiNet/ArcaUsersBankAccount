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
