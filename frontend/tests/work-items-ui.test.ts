import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { QueryState } from '../src/features/workspaces/shared.tsx'
import { ActivityChanges } from '../src/features/work-items/activity.tsx'
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

test('activity comparison aligns fields across asymmetric snapshots without inventing missing values', () => {
  const html = renderToStaticMarkup(
    createElement(ActivityChanges, {
      before: { Priority: 'High', AssigneeMembershipId: null },
      after: {
        Priority: 'Critical',
        Version: 2,
        Detail: { Type: 'ChangePriority' },
      },
    }),
  )
  assert.match(html, /scope="col">Before/)
  assert.match(html, /scope="col">After/)
  assert.match(
    html,
    /<tr><th scope="row">Priority<\/th><td><div>High<\/div><\/td><td><div>Critical<\/div><\/td><\/tr>/,
  )
  assert.match(
    html,
    /Assignee Membership ID[\s\S]*?Not set[\s\S]*?Not recorded/,
  )
  assert.match(
    html,
    /Version<\/th><td><div><span[^>]*>Not recorded<\/span><\/div><\/td><td><div>2/,
  )
  assert.match(html, /ChangePriority/)
})
test('activity comparison preserves booleans, zero, empty text, nested values and escapes HTML', () => {
  const html = renderToStaticMarkup(
    createElement(ActivityChanges, {
      before: null,
      after: {
        ConditionsMatched: false,
        Count: 0,
        Body: '',
        LabelIds: ['a', 'b'],
        Detail: { text: '<script>alert(1)</script>' },
      },
    }),
  )
  assert.match(html, /false/)
  assert.match(html, />0<\/div>/)
  assert.match(html, /Empty text/)
  assert.match(html, /Label IDs/)
  assert.match(html, /&lt;script&gt;/)
  assert.doesNotMatch(html, /<script>/)
})
