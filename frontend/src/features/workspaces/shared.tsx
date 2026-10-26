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
import { cn } from '@/lib/utils'
import { useFieldValidation } from '@/components/ui/use-field-validation'

export function ErrorNotice({
  error,
  message,
}: {
  error: unknown
  message?: string
}) {
  if (!error) return null
  const status = error instanceof ApiError ? error.status : 0
  const hint = message
    ? ''
    : status === 403
      ? 'Your permissions may have changed. Refresh to check your access.'
      : status === 404
        ? 'This resource is unavailable, or you no longer have access.'
        : status === 409
          ? 'Refresh the latest data before trying again.'
          : ''
  return (
    <div className="error-notice" role="alert">
      <div>
        {message ||
          (error instanceof Error ? error.message : 'The request failed.')}
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
  hasData = false,
}: {
  pending: boolean
  error: unknown
  retry: () => unknown
  children?: ReactNode
  hasData?: boolean
}) {
  if (pending && !hasData)
    return (
      <p role="status" className="workspace-empty">
        Loading…
      </p>
    )
  const transient =
    error instanceof ApiError &&
    (error.status === 0 || error.status === 429 || error.status >= 500)
  return (
    <>
      {error && (
        <div>
          <ErrorNotice error={error} />
          <Button variant="secondary" onClick={() => void retry()}>
            Refresh
          </Button>
        </div>
      )}
      {(!error || (hasData && transient)) && children}
    </>
  )
}
export function SelectField({
  label,
  children,
  error: serverError,
  onChange,
  onInvalid,
  ...props
}: SelectHTMLAttributes<HTMLSelectElement> & {
  label: string
  error?: string
}) {
  const id = useId()
  const validation = useFieldValidation<HTMLSelectElement>(
    label,
    serverError,
    onChange,
    onInvalid,
  )
  const error = validation.error
  return (
    <div className="form-field">
      <label htmlFor={id}>{label}</label>
      <select
        id={id}
        {...props}
        onChange={validation.onChange}
        onInvalid={validation.onInvalid}
        aria-invalid={!!error}
        aria-describedby={error ? `${id}-error` : undefined}
      >
        {children}
      </select>
      {error && (
        <p id={`${id}-error`} className="field-error" role="alert">
          {error}
        </p>
      )}
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
  triggerClassName,
  popupClassName,
}: {
  title: string
  description?: string
  trigger: string
  triggerClassName?: string
  popupClassName?: string
  children: (close: () => void) => ReactNode
}) {
  const [open, setOpen] = useState(false)
  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger
        className={cn('button button--secondary', triggerClassName)}
      >
        {trigger}
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Backdrop className="dialog-backdrop" />
        <Dialog.Popup
          className={cn('dialog-popup workspace-dialog', popupClassName)}
        >
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
  invalidateKeys = [['phase2']],
  disabled = false,
  submitClassName,
  actions,
  errorMessage,
}: {
  children: (fieldError: (name: string) => string | undefined) => ReactNode
  submit: (data: FormData) => Promise<unknown>
  label?: string
  done?: () => void
  onConflict?: (error: ApiError) => void
  invalidateKeys?: readonly (readonly unknown[])[]
  disabled?: boolean
  submitClassName?: string
  actions?: (busy: boolean) => ReactNode
  errorMessage?: (error: unknown) => string | undefined
}) {
  const client = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  // Async completion can close the form; no secrets or mutation results enter the query cache.
  const form = useRef<HTMLFormElement>(null)
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy || disabled) return
    const data = new FormData(
      event.currentTarget,
      (event.nativeEvent as SubmitEvent).submitter,
    )
    setBusy(true)
    setError(null)
    try {
      await submit(data)
      await Promise.all(
        invalidateKeys.map((queryKey) =>
          client.invalidateQueries({ queryKey }),
        ),
      )
      done?.()
    } catch (failure) {
      setError(failure)
      if (
        failure instanceof ApiError &&
        [403, 404, 409].includes(failure.status)
      ) {
        await Promise.all(
          invalidateKeys.map((queryKey) =>
            client.invalidateQueries({ queryKey }),
          ),
        )
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
        <ErrorNotice error={error} message={errorMessage?.(error)} />
        {error instanceof ApiError && Object.keys(error.fields).length > 0 && (
          <ul role="alert" className="error-notice error-summary">
            {Object.entries(error.fields).map(([name, messages]) => (
              <li key={name}>
                {name}: {messages.join(' ')}
              </li>
            ))}
          </ul>
        )}
        {actions ? (
          actions(busy)
        ) : (
          <Button
            type="submit"
            loading={busy}
            disabled={disabled}
            className={submitClassName}
          >
            {label}
          </Button>
        )}
      </fieldset>
    </form>
  )
}
export function ConfirmAction({
  title,
  description,
  label,
  run,
  invalidateKeys,
  triggerClassName,
}: {
  title: string
  description: string
  label: string
  run: () => Promise<unknown>
  invalidateKeys?: readonly (readonly unknown[])[]
  triggerClassName?: string
}) {
  return (
    <Modal
      title={title}
      description={description}
      trigger={label}
      triggerClassName={triggerClassName}
    >
      {(close) => (
        <ActionForm
          label={label}
          submit={run}
          done={close}
          invalidateKeys={invalidateKeys}
          submitClassName={triggerClassName}
        >
          {() => null}
        </ActionForm>
      )}
    </Modal>
  )
}
export function Empty({ children }: { children: ReactNode }) {
  return <p className="workspace-empty">{children}</p>
}
