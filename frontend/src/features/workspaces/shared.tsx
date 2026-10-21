import {
  useId,
  useRef,
  useState,
  type FormEvent,
  type ReactNode,
  type SelectHTMLAttributes,
} from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Dialog } from '@base-ui/react/dialog'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ApiError } from '@/lib/api'

export function ErrorNotice({ error }: { error: unknown }) {
  if (!error) return null
  const status = error instanceof ApiError ? error.status : 0
  const hint =
    status === 403
      ? 'Your permissions may have changed. Refresh to check your access.'
      : status === 404
        ? 'This resource is unavailable, or you no longer have access.'
        : status === 409
          ? 'Refresh the latest data before trying again.'
          : ''
  return (
    <div className="error-notice" role="alert">
      <div>
        {error instanceof Error ? error.message : 'The request failed.'}
        {hint && <p>{hint}</p>}
      </div>
    </div>
  )
}
export function QueryState({
  pending,
  error,
  retry,
  children,
}: {
  pending: boolean
  error: unknown
  retry: () => unknown
  children?: ReactNode
}) {
  if (pending)
    return (
      <p role="status" className="workspace-empty">
        Loading…
      </p>
    )
  if (error)
    return (
      <>
        <ErrorNotice error={error} />
        <Button variant="secondary" onClick={() => void retry()}>
          Refresh
        </Button>
      </>
    )
  return <>{children}</>
}
export function SelectField({
  label,
  children,
  ...props
}: SelectHTMLAttributes<HTMLSelectElement> & { label: string }) {
  const id = useId()
  return (
    <div className="form-field">
      <label htmlFor={id}>{label}</label>
      <select id={id} {...props}>
        {children}
      </select>
    </div>
  )
}
export function Pager({
  page,
  total,
  change,
}: {
  page: number
  total: number
  change: (page: number) => void
}) {
  return (
    <div className="workspace-pager">
      <span>
        {total} result{total === 1 ? '' : 's'} · Page {page} of{' '}
        {Math.max(1, Math.ceil(total / 20))}
      </span>
      <div className="workspace-actions">
        <Button
          variant="secondary"
          disabled={page <= 1}
          onClick={() => change(page - 1)}
        >
          Previous
        </Button>
        <Button
          variant="secondary"
          disabled={page * 20 >= total}
          onClick={() => change(page + 1)}
        >
          Next
        </Button>
      </div>
    </div>
  )
}
export function Modal({
  title,
  description,
  trigger,
  children,
}: {
  title: string
  description?: string
  trigger: string
  children: (close: () => void) => ReactNode
}) {
  const [open, setOpen] = useState(false)
  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger className="button button--secondary">
        {trigger}
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Backdrop className="dialog-backdrop" />
        <Dialog.Popup className="dialog-popup workspace-dialog">
          <Dialog.Title>{title}</Dialog.Title>
          <Dialog.Description className="muted">
            {description || 'Complete the fields below.'}
          </Dialog.Description>
          <Dialog.Close
            className="dialog-close icon-button"
            aria-label="Close dialog"
          >
            <X size={19} aria-hidden="true" />
          </Dialog.Close>
          {open && children(() => setOpen(false))}
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
export function ActionForm({
  children,
  submit,
  label = 'Save',
  done,
  onConflict,
}: {
  children: (fieldError: (name: string) => string | undefined) => ReactNode
  submit: (data: FormData) => Promise<unknown>
  label?: string
  done?: () => void
  onConflict?: (error: ApiError) => void
}) {
  const client = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  // Async completion can close the form; no secrets or mutation results enter the query cache.
  const form = useRef<HTMLFormElement>(null)
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    const data = new FormData(event.currentTarget)
    setBusy(true)
    setError(null)
    try {
      await submit(data)
      await client.invalidateQueries({ queryKey: ['phase2'] })
      done?.()
    } catch (failure) {
      setError(failure)
      if (
        failure instanceof ApiError &&
        [403, 404, 409].includes(failure.status)
      ) {
        await client.invalidateQueries({ queryKey: ['phase2'] })
        if (failure.status === 409) onConflict?.(failure)
      }
      requestAnimationFrame(() =>
        form.current
          ?.querySelector<HTMLElement>('[aria-invalid="true"]')
          ?.focus(),
      )
    } finally {
      setBusy(false)
    }
  }
  const fieldError = (name: string) =>
    error instanceof ApiError ? error.fields[name]?.join(' ') : undefined
  return (
    <form
      ref={form}
      onSubmit={save}
      className="workspace-form"
      aria-busy={busy}
    >
      <fieldset disabled={busy}>
        {children(fieldError)}
        <ErrorNotice error={error} />
        {error instanceof ApiError && Object.keys(error.fields).length > 0 && (
          <ul role="alert" className="field-error">
            {Object.entries(error.fields).map(([name, messages]) => (
              <li key={name}>
                {name}: {messages.join(' ')}
              </li>
            ))}
          </ul>
        )}
        <Button type="submit" loading={busy}>
          {label}
        </Button>
      </fieldset>
    </form>
  )
}
export function ConfirmAction({
  title,
  description,
  label,
  run,
}: {
  title: string
  description: string
  label: string
  run: () => Promise<unknown>
}) {
  return (
    <Modal title={title} description={description} trigger={label}>
      {(close) => (
        <ActionForm label={label} submit={run} done={close}>
          {() => null}
        </ActionForm>
      )}
    </Modal>
  )
}
export function Empty({ children }: { children: ReactNode }) {
  return <p className="workspace-empty">{children}</p>
}
