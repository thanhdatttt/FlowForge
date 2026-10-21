import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { QueryState } from '../src/features/workspaces/shared.tsx'
import { ApiError } from '../src/lib/api.ts'

test('transient background refetch errors keep the existing form visible with a retry action', () => {
  for (const status of [0, 429, 500, 503]) {
    const html = renderToStaticMarkup(
      createElement(
        QueryState,
        {
          pending: false,
          hasData: true,
          error: new ApiError('Retry later', status),
          retry: () => {},
        },
        createElement('textarea', { defaultValue: 'Unsaved draft' }),
      ),
    )
    assert.match(html, /Unsaved draft/)
    assert.match(html, /Retry later/)
    assert.match(html, /Refresh/)
  }
})
test('authorization and missing-resource errors hide stale content even when a previous read succeeded', () => {
  for (const status of [401, 403, 404]) {
    const html = renderToStaticMarkup(
      createElement(
        QueryState,
        {
          pending: false,
          hasData: true,
          error: new ApiError('No access', status),
          retry: () => {},
        },
        createElement('p', null, 'Private work item'),
      ),
    )
    assert.doesNotMatch(html, /Private work item/)
    assert.match(html, /No access/)
  }
})
test('initial query failure never exposes children without a successful result', () => {
  const html = renderToStaticMarkup(
    createElement(
      QueryState,
      {
        pending: false,
        hasData: false,
        error: new ApiError('Offline', 0),
        retry: () => {},
      },
      createElement('textarea', { defaultValue: 'Uninitialized form' }),
    ),
  )
  assert.doesNotMatch(html, /Uninitialized form/)
})
