const esbuild = require('esbuild');

const isWatch = process.argv.includes('--watch');

/** @type {import('esbuild').BuildOptions} */
const extensionOptions = {
  entryPoints: ['src/extension.ts'],
  bundle: true,
  outfile: 'dist/extension.js',
  external: ['vscode'],
  format: 'cjs',
  platform: 'node',
  target: 'node18',
  sourcemap: true,
  minify: false,
  logLevel: 'info',
};

/** @type {import('esbuild').BuildOptions} */
const mcpServerOptions = {
  entryPoints: ['unity-mcp.js'],
  bundle: true,
  outfile: 'dist/unity-mcp.js',
  format: 'cjs',
  platform: 'node',
  target: 'node18',
  sourcemap: false,
  minify: false,
  logLevel: 'info',
};

if (isWatch) {
  Promise.all([
    esbuild.context(extensionOptions).then(ctx => ctx.watch()),
    esbuild.context(mcpServerOptions).then(ctx => ctx.watch()),
  ]).then(() => console.log('Watching for changes...'));
} else {
  Promise.all([
    esbuild.build(extensionOptions),
    esbuild.build(mcpServerOptions),
  ]).catch(() => process.exit(1));
}
