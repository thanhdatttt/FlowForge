import {
  useId,
  type ComponentProps,
  type ReactNode,
  type TextareaHTMLAttributes,
} from 'react'
import { ActionForm, ConfirmAction } from '@/features/workspaces/shared'
import type { Scope, WorkItem } from './api'
import { invalidateKeys } from './queries'

export function WorkForm({
  scope,
  ...props
}: Omit<ComponentProps<typeof ActionForm>, 'invalidateKeys'> & {
  scope: Scope
}) {
  return <ActionForm {...props} invalidateKeys={invalidateKeys(scope)} />
}
export function WorkConfirm({
  scope,
  ...props
}: Omit<ComponentProps<typeof ConfirmAction>, 'invalidateKeys'> & {
  scope: Scope
}) {
  return <ConfirmAction {...props} invalidateKeys={invalidateKeys(scope)} />
}
export function TextArea({
  label,
  error,
  hint,
  ...props
}: TextareaHTMLAttributes<HTMLTextAreaElement> & {
  label: string
  error?: string
  hint?: string
}) {
  const id = useId()
  return (
    <div className="form-field workspace-textarea">
      <label htmlFor={id}>{label}</label>
      <textarea
        {...props}
        id={id}
        aria-invalid={!!error}
        aria-describedby={error || hint ? `${id}-description` : undefined}
      />
      {(error || hint) && (
        <p
          id={`${id}-description`}
          className={error ? 'field-error' : 'field-hint'}
        >
          {error || hint}
        </p>
      )}
    </div>
  )
}
export function WorkLink({
  href,
  navigate,
  children,
  ...props
}: {
  href: string
  navigate: (path: string) => void
  children: ReactNode
} & Omit<ComponentProps<'a'>, 'href'>) {
  return (
    <a
      {...props}
      href={href}
      onClick={(event) => {
        if (
          event.button === 0 &&
          !event.metaKey &&
          !event.ctrlKey &&
          !event.shiftKey &&
          !event.altKey
        ) {
          event.preventDefault()
          navigate(href)
        }
      }}
    >
      {children}
    </a>
  )
}
export function ItemBadges({ item }: { item: WorkItem }) {
  return (
    <div className="workspace-actions work-badges">
      <span className="workspace-badge">{item.type}</span>
      <span className="workspace-badge" data-status={item.status}>
        {item.status.replace(/([a-z])([A-Z])/g, '$1 $2')}
      </span>
      <span className="workspace-badge" data-priority={item.priority}>
        {item.priority}
      </span>
      {item.isBlocked && (
        <span className="workspace-badge" data-status="Blocked">
          Blocked by dependency
        </span>
      )}
      {item.needsReassignment && (
        <span className="workspace-badge" data-status="Pending">
          Needs reassignment
        </span>
      )}
      {item.archivedAt && (
        <span className="workspace-badge" data-status="archived">
          Archived
        </span>
      )}
    </div>
  )
}
