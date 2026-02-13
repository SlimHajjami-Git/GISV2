import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'tn.belive.gisv2',
  appName: 'GIS Fleet',
  webDir: 'www',
  server: {
    // For dev: proxy to local API. Remove in production.
    // url: 'http://192.168.1.X:4200',
    cleartext: true
  },
  plugins: {
    PushNotifications: {
      presentationOptions: ['badge', 'sound', 'alert']
    },
    Geolocation: {
      permissions: ['location', 'coarseLocation']
    }
  }
};

export default config;
