import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell, X } from 'lucide-react'
import { Dialog } from '@base-ui/react/dialog'
import { Button } from '@/components/ui/button'
import { invitationApi } from '@/features/workspaces/api'
import { InvitationEntry } from '@/features/workspaces/invitation-page'
import type { InvitationLink } from '@/features/workspaces/rules'
import { Empty, ErrorNotice, Pager, QueryState, SelectField } from '@/features/workspaces/shared'
import { notificationApi, notificationKey, notificationLabel, notificationPath, type Notification } from './api'
import type { LiveState } from './realtime'
import './notifications.css'

function InvitationNotice({ id, open }: { id: string; open: (link: InvitationLink) => void }) {
  const query = useQuery({
    queryKey: ['phase2', 'invitation', id],
    queryFn: ({ signal }) => invitationApi.get(id, signal),
    retry: false,
  })
  return <section className="notification-invitation">
    <h3>Workspace invitation</h3>
    <QueryState pending={query.isPending} error={query.error} retry={query.refetch} hasData={!!query.data}>
      {query.data && <>
        <p>Invitation for <strong>{query.data.email}</strong> as <strong>{query.data.role}</strong>.</p>
        <p className="muted">{query.data.status} · Expires {new Date(query.data.expiresAt).toLocaleString()}.</p>
        <p>Open the original invitation link from your email or team, or paste it below to respond.</p>
        <InvitationEntry open={open} />
      </>}
    </QueryState>
  </section>
}
export function LiveStatus({ state, retry }: { state: LiveState; retry: () => void }) {
  const labels = { connecting: 'Connecting live updates…', connected: 'Live updates connected', reconnecting: 'Reconnecting live updates…', offline: 'Live updates unavailable' }
  return <div className="live-status" data-state={state}>
    <span role="status"><span aria-hidden="true" className="live-status-dot" />{labels[state]}</span>
    {state !== 'connected' && <Button variant="text" onClick={retry}>Retry live updates</Button>}
  </div>
}
export function NotificationCenter({ navigate, openInvitation }: {
  navigate: (path: string) => void
  openInvitation: (link: InvitationLink) => void
}) {
  const client = useQueryClient()
  const [open, setOpen] = useState(false)
  const [unread, setUnread] = useState(false)
  const [page, setPage] = useState(1)
  const [invitation, setInvitation] = useState<string | null>(null)
  const [notice, setNotice] = useState('')
  const badge = useQuery({
    queryKey: [...notificationKey, 'unread-count'],
    queryFn: ({ signal }) => notificationApi.list(1, true, 1, signal),
    retry: false,
    refetchInterval: 60_000,
  })
  const list = useQuery({
    queryKey: [...notificationKey, 'list', unread, page],
    queryFn: ({ signal }) => notificationApi.list(page, unread, 20, signal),
    enabled: open,
    retry: false,
    refetchInterval: open ? 60_000 : false,
  })
  // A read/all-read or permission change can empty the final page.
  const maxPage = Math.max(1, Math.ceil((list.data?.total || 0) / 20))
  if (list.data && page > maxPage) setPage(maxPage)
  const read = useMutation({
    mutationFn: (id: string | null) => id ? notificationApi.read(id) : notificationApi.readAll(),
    retry: false,
    onSuccess: (_, id) => setNotice(id ? 'Notification marked as read.' : 'All available notifications marked as read.'),
    onSettled: () => client.invalidateQueries({ queryKey: notificationKey }),
  })
  function follow(item: Notification) {
    if (item.invitationId) { setInvitation(item.invitationId); return }
    const path = notificationPath(item)
    if (path) { setOpen(false); navigate(path) }
  }
  const count = badge.isError ? undefined : badge.data?.total
  return <Dialog.Root open={open} onOpenChange={value => {
    setOpen(value)
    if (value) { setInvitation(null); setNotice(''); read.reset(); void client.invalidateQueries({ queryKey: notificationKey }) }
  }}>
    <Dialog.Trigger className="button button--secondary notification-trigger" aria-label={`Notifications${count === undefined ? ', unread count unavailable' : `, ${count} unread`}`}>
      <Bell size={17} aria-hidden="true" /> Notifications
      {count !== undefined && count > 0 && <span className="notification-count" aria-hidden="true">{count > 99 ? '99+' : count}</span>}
    </Dialog.Trigger>
    <Dialog.Portal>
      <Dialog.Backdrop className="dialog-backdrop" />
      <Dialog.Popup className="dialog-popup workspace-dialog notification-dialog">
        <Dialog.Title>Notifications</Dialog.Title>
        <Dialog.Description className="muted">Assignments, mentions, overdue work and workspace invitations.</Dialog.Description>
        <Dialog.Close className="dialog-close icon-button" aria-label="Close notifications"><X size={19} aria-hidden="true" /></Dialog.Close>
        <div className="notification-toolbar">
          <SelectField label="Show notifications" value={unread ? 'unread' : 'all'} onChange={event => { setUnread(event.target.value === 'unread'); setPage(1) }}>
            <option value="all">All notifications</option><option value="unread">Unread only</option>
          </SelectField>
          <Button variant="secondary" disabled={count === 0 || read.isPending} onClick={() => read.mutate(null)}>Mark all as read</Button>
          <Button variant="text" onClick={() => void client.invalidateQueries({ queryKey: notificationKey })}>Refresh notifications</Button>
        </div>
        <ErrorNotice error={badge.error} />
        <ErrorNotice error={read.error} />
        <p className="notification-feedback" role="status">{notice}</p>
        {invitation && <InvitationNotice key={invitation} id={invitation} open={link => { setOpen(false); openInvitation(link) }} />}
        <QueryState pending={list.isPending} error={list.error} retry={list.refetch} hasData={!!list.data}>
          {!list.data?.items.length && <Empty>{unread ? 'No unread notifications.' : 'No notifications yet.'}</Empty>}
          <ul className="notification-list" aria-label="Notifications">
            {list.data?.items.map(item => <li key={item.id} data-unread={!item.readAt}>
              <div className="notification-copy">
                <strong>{notificationLabel(item.type)}</strong>
                <p className="muted"><time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleString()}</time> · {item.readAt ? 'Read' : 'Unread'}</p>
              </div>
              <div className="workspace-actions">
                {(item.invitationId || notificationPath(item)) && <Button variant="text" onClick={() => follow(item)}>{item.invitationId ? 'Review invitation' : 'Open work item'}</Button>}
                {!item.readAt && <Button variant="secondary" disabled={read.isPending} onClick={() => read.mutate(item.id)}>Mark as read</Button>}
              </div>
            </li>)}
          </ul>
          {list.data && <Pager page={page} total={list.data.total} change={setPage} />}
        </QueryState>
      </Dialog.Popup>
    </Dialog.Portal>
  </Dialog.Root>
}
