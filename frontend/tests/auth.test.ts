import { afterEach, test } from 'node:test'
import assert from 'node:assert/strict'
import { validateCredentials } from '../src/features/auth/validation.ts'
import { authApi } from '../src/features/auth/api.ts'
import { api, ApiError } from '../src/lib/api.ts'
import {
  useSessionStore,
  type AuthTokens,
} from '../src/features/auth/session-store.ts'

const originalFetch = globalThis.fetch
const tokens: AuthTokens = {
  accessToken: 'test-access',
  refreshToken: 'test-refresh',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
}
const rotated = {
  ...tokens,
  accessToken: 'rotated-access',
  refreshToken: 'rotated-refresh',
}
const user = {
  id: 'test-user',
  email: 'alex@example.test',
  displayName: 'Alex',
  emailVerified: false,
}
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
afterEach(() => {
  globalThis.fetch = originalFetch
  useSessionStore.getState().clear()
})

test('sign-in does not apply new-password complexity rules to existing accounts', () => {
  assert.deepEqual(
    validateCredentials({ email: user.email, password: 'old' }, false),
    {},
  )
})
test('registration validates missing name, email, and password complexity', () => {
  const errors = validateCredentials(
    { email: 'bad', password: 'weak', displayName: ' ' },
    true,
  )
  assert.deepEqual(Object.keys(errors).sort(), [
    'displayName',
    'email',
    'password',
  ])
  assert.deepEqual(
    validateCredentials(
      { email: user.email, password: 'ValidPass1!', displayName: 'Alex' },
      true,
    ),
    {},
  )
})
test('whitespace is not a password symbol; Unicode punctuation is accepted', () => {
  assert.ok(
    validateCredentials(
      { email: user.email, password: 'ValidPass1 ', displayName: 'Alex' },
      true,
    ).password,
  )
  assert.deepEqual(
    validateCredentials(
      { email: user.email, password: 'ValidPass1。', displayName: 'Alex' },
      true,
    ),
    {},
  )
})
test('sign-in sends only the documented login fields', async () => {
  globalThis.fetch = async (url, init) => {
    assert.equal(url, '/api/v1/auth/login')
    assert.equal(init?.method, 'POST')
    assert.deepEqual(JSON.parse(init?.body as string), {
      email: user.email,
      password: 'example',
    })
    return json(tokens)
  }
  assert.deepEqual(
    await authApi.signIn({
      email: user.email,
      password: 'example',
      displayName: 'Ignored',
    }),
    tokens,
  )
})
test('registration uses the documented endpoint and response', async () => {
  globalThis.fetch = async (url, init) => {
    assert.equal(url, '/api/v1/auth/register')
    assert.equal(JSON.parse(init?.body as string).displayName, 'Alex')
    return json(tokens, 201)
  }
  assert.deepEqual(
    await authApi.register({
      email: user.email,
      password: 'ValidPass1!',
      displayName: 'Alex',
    }),
    tokens,
  )
})
test('invalid credentials remain generic and never trigger token refresh', async () => {
  let calls = 0
  globalThis.fetch = async () => {
    calls++
    return json({ title: 'Invalid email or password.' }, 401)
  }
  await assert.rejects(authApi.signIn({ email: user.email, password: 'bad' }), {
    message: 'Invalid email or password.',
    status: 401,
  })
  assert.equal(calls, 1)
})
test('validation problem responses retain field errors', async () => {
  globalThis.fetch = async () =>
    json(
      {
        title: 'Validation failed',
        errors: { email: ['Enter a valid email.'] },
      },
      400,
    )
  await assert.rejects(
    authApi.register({ email: '', password: '', displayName: '' }),
    (error: unknown) =>
      error instanceof ApiError &&
      error.fields.email?.[0] === 'Enter a valid email.',
  )
})
test('duplicate registration returns a useful conflict', async () => {
  globalThis.fetch = async () =>
    json({ title: 'Email already registered.' }, 409)
  await assert.rejects(
    authApi.register({
      email: user.email,
      password: 'ValidPass1!',
      displayName: 'Alex',
    }),
    { status: 409, message: 'Email already registered.' },
  )
})
test('expired tokens rotate once for concurrent authenticated reads', async () => {
  useSessionStore
    .getState()
    .setTokens({ ...tokens, accessTokenExpiresAt: '2000-01-01T00:00:00Z' })
  let refreshes = 0
  globalThis.fetch = async (url, init) => {
    if (url === '/api/v1/auth/refresh') {
      refreshes++
      await new Promise((resolve) => setTimeout(resolve, 5))
      return json(rotated)
    }
    assert.equal(
      (init?.headers as Record<string, string> | undefined)?.Authorization,
      'Bearer rotated-access',
    )
    return json(user)
  }
  assert.deepEqual(await Promise.all([authApi.me(), authApi.me()]), [
    user,
    user,
  ])
  assert.equal(refreshes, 1)
  assert.equal(
    useSessionStore.getState().tokens?.refreshToken,
    rotated.refreshToken,
  )
})
test('401 on a protected resource refreshes and retries once', async () => {
  useSessionStore.getState().setTokens(tokens)
  let reads = 0
  globalThis.fetch = async (url) =>
    url === '/api/v1/auth/refresh'
      ? json(rotated)
      : ++reads === 1
        ? json({}, 401)
        : json(user)
  assert.deepEqual(await authApi.me(), user)
  assert.equal(reads, 2)
})
test('invalid refresh credentials clear the session', async () => {
  useSessionStore
    .getState()
    .setTokens({ ...tokens, accessTokenExpiresAt: '2000-01-01T00:00:00Z' })
  globalThis.fetch = async () => json({ title: 'Invalid refresh token.' }, 401)
  await assert.rejects(authApi.me(), { status: 401 })
  assert.equal(useSessionStore.getState().tokens, null)
})
test('a refresh that completes after sign-out cannot restore the session', async () => {
  useSessionStore
    .getState()
    .setTokens({ ...tokens, accessTokenExpiresAt: '2000-01-01T00:00:00Z' })
  globalThis.fetch = async () => {
    useSessionStore.getState().clear()
    return json(rotated)
  }
  await assert.rejects(authApi.me(), { status: 401 })
  assert.equal(useSessionStore.getState().tokens, null)
})
test('persistent authorization failure retries only once and clears the session', async () => {
  useSessionStore.getState().setTokens(tokens)
  let reads = 0
  let refreshes = 0
  globalThis.fetch = async (url) => {
    if (url === '/api/v1/auth/refresh') {
      refreshes++
      return json(rotated)
    }
    reads++
    return json({}, 401)
  }
  await assert.rejects(authApi.me(), { status: 401 })
  assert.equal(reads, 2)
  assert.equal(refreshes, 1)
  assert.equal(useSessionStore.getState().tokens, null)
})

test('a transient refresh failure keeps the session available for retry', async () => {
  useSessionStore
    .getState()
    .setTokens({ ...tokens, accessTokenExpiresAt: '2000-01-01T00:00:00Z' })
  globalThis.fetch = async () => json({}, 503)
  await assert.rejects(authApi.me(), { status: 503 })
  assert.equal(
    useSessionStore.getState().tokens?.refreshToken,
    tokens.refreshToken,
  )
})

test('protected requests without a session never reach the server', async () => {
  globalThis.fetch = async () => {
    throw new Error('Unexpected request')
  }
  await assert.rejects(api('/auth/me', {}, true), { status: 401 })
})
test('verification and logout handle empty 204 responses and preserve leading zeroes', async () => {
  useSessionStore.getState().setTokens(tokens)
  const paths: string[] = []
  globalThis.fetch = async (url, init) => {
    paths.push(String(url))
    if (String(url).endsWith('/confirm'))
      assert.equal(JSON.parse(init?.body as string).code, '012345')
    if (String(url).endsWith('/logout'))
      assert.equal(
        JSON.parse(init?.body as string).refreshToken,
        tokens.refreshToken,
      )
    return new Response(null, { status: 204 })
  }
  await authApi.requestCode()
  await authApi.confirmCode('012345')
  await authApi.signOut(tokens.refreshToken)
  assert.deepEqual(paths, [
    '/api/v1/auth/email-verification/request',
    '/api/v1/auth/email-verification/confirm',
    '/api/v1/auth/logout',
  ])
})
test('rate limits, offline failures, and unavailable services have actionable messages', async () => {
  globalThis.fetch = async () => new Response('', { status: 429 })
  await assert.rejects(
    authApi.signIn({ email: user.email, password: 'example' }),
    /wait a minute/,
  )
  globalThis.fetch = async () => {
    throw new TypeError('Failed to fetch')
  }
  await assert.rejects(authApi.me(), { status: 401 })
  await assert.rejects(
    authApi.signIn({ email: user.email, password: 'example' }),
    /Check your connection/,
  )
  globalThis.fetch = async () =>
    json({ title: 'Private server exception' }, 503)
  await assert.rejects(
    authApi.signIn({ email: user.email, password: 'example' }),
    /unavailable right now/,
  )
})
