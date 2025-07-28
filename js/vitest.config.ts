import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    include: ['test/**/*.test.ts'],
    setupFiles: ['test/setup.vitest.ts'],
    hookTimeout: 120_000,          // ← applies to every before/after hook
    testTimeout: 120_000           // ← and to each individual test
  }
})