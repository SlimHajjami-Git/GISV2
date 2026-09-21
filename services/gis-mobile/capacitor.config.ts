import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'tn.belive.gisv2',
  appName: 'Calypso',
  webDir: 'www',
  server: {
    // Serve the WebView over http://localhost (not https://localhost, which
    // is Capacitor's Android default). Required because our API/SignalR
    // server at 41.231.5.146 is plain HTTP: an HTTPS-origin WebView
    // refuses to open ws:// connections (mixed content), which was the
    // root cause of the "failed to construct websocket, an insecure
    // websocket connection may not be initiated from a page loaded over
    // https" error. Once the server gets a real TLS cert, revert this.
    androidScheme: 'http',
    cleartext: true
  },
  android: {
    // Exigé par @capacitor-community/background-geolocation (README, issue #89) : sans
    // le pont « legacy », Android cesse de livrer les positions à la WebView après
    // ~5 min en arrière-plan. CapacitorHttp (ci-dessous) est l'autre moitié de la
    // consigne : les envois passent par le HTTP natif, que l'arrière-plan ne bride pas.
    useLegacyBridge: true
  },
  plugins: {
    CapacitorHttp: {
      enabled: true
    },
    PushNotifications: {
      presentationOptions: ['badge', 'sound', 'alert']
    },
    Geolocation: {
      permissions: ['location', 'coarseLocation']
    }
  }
};

export default config;
