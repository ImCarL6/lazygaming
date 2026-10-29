"""Testa transformação em memória com fixture; não prepara pacote nem inicia apps."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('moonlight_adapter', Path(__file__).resolve().parents[1] / 'scripts/Preparar-Moonlight.py')
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


class AdaptationTests(unittest.TestCase):
    source = '''function handleDeepLink() {
  if (!_isSmartHubSupported) {
    return;
  }
  navigate();
}
function probeSmartHubSupport() {
  tizen.application.launchAppControl(service);
}
function onWindowLoad() {
  probeSmartHubSupport().then(handleDeepLink);
  window.addEventListener('appcontrol', handleDeepLink);
}
'''

    def test_no_service_launch_and_deep_link_preserved(self):
        result = adapter.adapt_javascript(self.source)
        self.assertNotIn('launchAppControl', result)
        self.assertNotIn('if (!_isSmartHubSupported)', result)
        self.assertIn('_isSmartHubSupported = false', result)
        self.assertIn('return Promise.resolve()', result)
        self.assertIn('navigate();', result)
        self.assertIn("window.addEventListener('appcontrol', handleDeepLink)", result)

    def test_changed_or_already_adapted_source_is_rejected(self):
        for source in [self.source.replace('function probeSmartHubSupport()', 'function changed()'),
                       self.source + self.source, adapter.adapt_javascript(self.source)]:
            with self.assertRaises(ValueError):
                adapter.adapt_javascript(source)


if __name__ == '__main__':
    unittest.main()
