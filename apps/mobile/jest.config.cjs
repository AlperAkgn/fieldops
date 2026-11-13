module.exports = {
  preset: 'jest-expo',
  testMatch: ['<rootDir>/src/**/*.spec.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transformIgnorePatterns: [
    'node_modules/(?!((jest-)?react-native|@react-native(-community)?|expo.*|@expo(nent)?/.*|@react-navigation/.*|react-native-safe-area-context|react-native-screens)/)',
  ],
};
