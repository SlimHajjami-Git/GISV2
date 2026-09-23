module.exports = {
  preset: 'jest-preset-angular',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
  testPathIgnorePatterns: ['<rootDir>/node_modules/'],
  // « marked » n'est publie qu'en ESM : sans cette exception, Jest le lit tel quel
  // et echoue sur « Unexpected token 'export' ». Le paquet est tire des que l'on
  // importe app.routes (reports.component et ai-fleet-report l'utilisent), donc
  // tout test qui touche au routage tombait dessus.
  // Le « .*\.mjs$ » vient du preset Angular : le reecraser sans lui casse
  // jest-preset-angular lui-meme (setup-env/zone est en ESM).
  transformIgnorePatterns: ['node_modules/(?!(.*\\.mjs$|marked))'],
  moduleNameMapper: {
    '^src/(.*)$': '<rootDir>/src/$1'
  },
  collectCoverageFrom: [
    'src/**/*.ts',
    '!src/**/*.module.ts',
    '!src/main.ts',
    '!src/**/*.d.ts'
  ],
  coverageDirectory: 'coverage',
  testMatch: ['**/*.spec.ts']
};
