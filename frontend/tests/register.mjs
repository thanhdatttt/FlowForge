import { registerHooks } from 'node:module'
import { existsSync, readFileSync } from 'node:fs'
import ts from 'typescript'

// Node 24 strips TypeScript; mirror Vite's @ alias and extension resolution.
registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.startsWith('@/')) {
      for (const extension of ['.ts', '.tsx']) {
        const candidate = new URL(
          `../src/${specifier.slice(2)}${extension}`,
          import.meta.url,
        )
        if (existsSync(candidate)) return nextResolve(candidate.href, context)
      }
    }
    if (
      specifier.startsWith('.') &&
      context.parentURL &&
      !/\.[a-z]+$/i.test(specifier)
    ) {
      for (const extension of ['.ts', '.tsx']) {
        const candidate = new URL(`${specifier}${extension}`, context.parentURL)
        if (existsSync(candidate)) return nextResolve(candidate.href, context)
      }
    }
    return nextResolve(specifier, context)
  },
  load(url, context, nextLoad) {
    if (url.endsWith('.tsx')) {
      return {
        format: 'module',
        source: ts.transpileModule(readFileSync(new URL(url), 'utf8'), {
          compilerOptions: {
            jsx: ts.JsxEmit.ReactJSX,
            module: ts.ModuleKind.ESNext,
            target: ts.ScriptTarget.ES2023,
          },
          fileName: new URL(url).pathname,
        }).outputText,
        shortCircuit: true,
      }
    }
    return nextLoad(url, context)
  },
})
