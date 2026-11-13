import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import plugins from 'expo/config-plugins';
import { validationOrigin, validationCa } from '../scripts/validation-tls.mjs';

const { AndroidConfig, withAndroidManifest, withDangerousMod } = plugins;

export default function withValidationTls(config) {
  if (process.env.FIELDOPS_ANDROID_VALIDATION !== '1') return config;

  const { hostname } = validationOrigin(process.env.EXPO_PUBLIC_API_BASE_URL);
  const ca = validationCa(process.env.FIELDOPS_ANDROID_CA_DER_BASE64);
  config = withAndroidManifest(config, (mod) => {
    const app = AndroidConfig.Manifest.getMainApplicationOrThrow(mod.modResults);
    app.$['android:usesCleartextTraffic'] = 'false';
    if (ca) app.$['android:networkSecurityConfig'] = '@xml/fieldops_validation_network_security';
    return mod;
  });

  if (!ca) return config;

  return withDangerousMod(config, [
    'android',
    (mod) => {
      const res = join(mod.modRequest.platformProjectRoot, 'app', 'src', 'main', 'res');
      mkdirSync(join(res, 'raw'), { recursive: true });
      mkdirSync(join(res, 'xml'), { recursive: true });
      writeFileSync(join(res, 'raw', 'fieldops_validation_ca.cer'), ca.raw);
      writeFileSync(
        join(res, 'xml', 'fieldops_validation_network_security.xml'),
        `<?xml version="1.0" encoding="utf-8"?>
<network-security-config>
  <base-config cleartextTrafficPermitted="false" />
  <domain-config cleartextTrafficPermitted="false">
    <domain includeSubdomains="false">${hostname}</domain>
    <trust-anchors>
      <certificates src="@raw/fieldops_validation_ca" />
    </trust-anchors>
  </domain-config>
</network-security-config>
`,
      );
      return mod;
    },
  ]);
}
