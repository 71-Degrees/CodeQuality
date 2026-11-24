import js from '@eslint/js';
import globals from 'globals';

// The one lint rule set for every repo's browser scripts (src/*/wwwroot/js), run by CodeQuality from its published copy (`quality scripts`):
// classic browser scripts (not modules) that share one page global each. The rules kept are the ones that catch bugs; no-implicit-globals in
// particular: a top-level function in a classic script becomes a window property, and that is how Leaflet's own `window.leaflet` replaced
// CrateDigger's map helper (2026-11-16). It is the union of what TriviaNight and CrateDigger ran on their own until 2026-11-24 (decision D2
// in docs/plans/standalone-quality.md). A repo's page globals (its own namespace, L for Leaflet, Blazor) come from its quality.json
// "scriptGlobals" and reach ESLint as --global flags. Vendored code under wwwroot/lib is not ours and is not linted.
export default [
    { ignores: ['**/lib/**'] },
    js.configs.recommended,
    {
        files: ['**/*.js'],
        languageOptions: {
            ecmaVersion: 2022,
            sourceType: 'script',
            globals: { ...globals.browser },
        },
        rules: {
            eqeqeq: ['error', 'always'],
            'no-unused-vars': ['error', { args: 'none', caughtErrors: 'none' }],
            'no-shadow': 'error',
            'no-implicit-globals': 'error',
            'no-throw-literal': 'error',
            'no-unsafe-finally': 'error',
            'no-empty': ['error', { allowEmptyCatch: false }],
            'prefer-const': 'error',
            'no-var': 'error',
            'require-atomic-updates': 'off',
        },
    },
];
