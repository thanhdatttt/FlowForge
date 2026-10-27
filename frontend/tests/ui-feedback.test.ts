import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ApiError } from '../src/lib/api.ts'
import { memberErrorMessage } from '../src/features/workspaces/rules.ts'
import { ActionForm, ErrorNotice } from '../src/features/workspaces/shared.tsx'
import { Button } from '../src/components/ui/button.tsx'
import { Attachments } from '../src/features/work-items/attachments.tsx'

test('add-member copy explains duplicate accounts and ambiguous missing-account/access errors without modifying the API error', () => {
  const duplicate = new ApiError('Conflict', 409)
  assert.match(
    memberErrorMessage(duplicate, 'workspace')!,
    /already a member of this workspace/,
  )
  assert.equal(duplicate.message, 'Conflict')
  assert.equal(duplicate.status, 409)
  const missing = memberErrorMessage(
    new ApiError('Not found', 404),
    'workspace',
  )!
  assert.match(missing, /active account/)
  assert.match(missing, /no longer have access/)
  assert.match(missing, /invitation/)
  assert.match(
    memberErrorMessage(new ApiError('Forbidden', 403), 'workspace')!,
    /permission/,
  )
  assert.equal(
    memberErrorMessage(new ApiError('Offline', 0), 'workspace'),
    undefined,
  )
})

test('project add-member copy distinguishes archived projects from existing membership and retains safe fallback errors', () => {
  assert.match(
    memberErrorMessage(
      new ApiError('The user is already a project member.', 409),
      'project',
    )!,
    /already a member of this project/,
  )
  assert.match(
    memberErrorMessage(
      new ApiError('Archived projects are read-only.', 409),
      'project',
    )!,
    /Restore it/,
  )
  assert.match(
    memberErrorMessage(new ApiError('Changed', 409), 'project')!,
    /Refresh/,
  )
  assert.match(
    memberErrorMessage(new ApiError('Invalid user', 400), 'project')!,
    /active workspace member/,
  )
  assert.equal(
    memberErrorMessage(new ApiError('Unavailable', 503), 'project'),
    undefined,
  )
})

test('friendly member errors replace generic resource/conflict hints while preserving the alert', () => {
  const error = new ApiError('Not found', 404)
  const html = renderToStaticMarkup(
    createElement(ErrorNotice, {
      error,
      message: memberErrorMessage(error, 'workspace'),
    }),
  )
  assert.match(html, /role="alert"/)
  assert.match(html, /active account/)
  assert.doesNotMatch(html, /Not found|This resource is unavailable/)
})

test('response actions use distinct named submit buttons; ordinary ActionForm still renders its original submit', () => {
  const client = new QueryClient()
  const render = (
    actions?: (busy: boolean) => ReturnType<typeof createElement>,
  ) => {
    const props = {
      submit: async () => {},
      label: 'Confirm response',
      children: () => null,
      actions,
    }
    return renderToStaticMarkup(
      createElement(
        QueryClientProvider,
        { client },
        createElement(ActionForm, props),
      ),
    )
  }
  const html = render((busy) =>
    createElement(
      'div',
      { className: 'workspace-invitation-actions' },
      createElement(
        Button,
        {
          type: 'submit',
          name: 'response',
          value: 'accept',
          disabled: busy,
          className: 'button--enable',
        },
        'Accept invitation',
      ),
      createElement(
        Button,
        {
          type: 'submit',
          name: 'response',
          value: 'reject',
          disabled: busy,
          className: 'button--danger',
        },
        'Decline invitation',
      ),
    ),
  )
  assert.match(
    html,
    /<button(?=[^>]*name="response")(?=[^>]*value="accept")[^>]*>/,
  )
  assert.match(
    html,
    /<button(?=[^>]*name="response")(?=[^>]*value="reject")[^>]*>/,
  )
  assert.match(html, /button--enable/)
  assert.match(html, /button--danger/)
  assert.doesNotMatch(html, /type="radio"|Confirm response/)
  assert.match(render(), /type="submit"[^>]*>Confirm response/)
  client.clear()
})

test('styled attachment picker keeps its native required input, accepted formats and upload action', () => {
  const client = new QueryClient()
  const scope = { workspaceId: 'workspace', projectId: 'project' }
  client.setQueryData(
    ['phase3', 'workspace', 'project', 'attachments', 'item', 1],
    { items: [], total: 0 },
  )
  const html = renderToStaticMarkup(
    createElement(
      QueryClientProvider,
      { client },
      createElement(Attachments, {
        scope,
        itemId: 'item',
        userId: 'owner',
        role: 'Owner',
        writable: true,
      }),
    ),
  )
  assert.match(html, /work-upload-picker/)
  assert.match(html, /type="file"/)
  assert.match(html, /name="file"/)
  assert.match(html, /required=""/)
  assert.match(html, /image\/png,image\/jpeg,application\/pdf,text\/plain/)
  assert.match(html, /Maximum 10 MiB/)
  assert.match(html, /Upload file/)
  client.clear()
})
