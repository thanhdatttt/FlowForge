import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import {
  Empty,
  Modal,
  Pager,
  QueryState,
  SelectField,
} from '@/features/workspaces/shared'
import type { Role } from '@/features/workspaces/api'
import { workApi, type Comment, type Scope } from './api'
import { canModerate, mentionParts, timestamp } from './rules'
import { TextArea, WorkConfirm, WorkForm } from './shared'
import { useMembers, workKey } from './queries'

function CommentEditor({
  scope,
  itemId,
  comment: current,
  close,
}: {
  scope: Scope
  itemId: string
  comment?: Comment
  close?: () => void
}) {
  const [comment] = useState(current)
  const [body, setBody] = useState(comment?.body || '')
  const [mention, setMention] = useState('')
  const [conflict, setConflict] = useState(false)
  const members = useMembers(scope)
  return (
    <>
      {conflict && (
        <p className="error-notice" role="alert">
          The comment changed. Your draft is kept below; copy it if needed, then
          close and reopen to edit the latest version.
        </p>
      )}
      <WorkForm
        scope={scope}
        label={comment ? 'Save comment' : 'Post comment'}
        disabled={conflict}
        onConflict={() => setConflict(true)}
        submit={() => workApi.saveComment(scope, itemId, body.trim(), comment)}
        done={() => {
          setBody('')
          setMention('')
          close?.()
        }}
      >
        {(error) => (
          <>
            <TextArea
              label={comment ? 'Edit comment' : 'Write a comment'}
              name="body"
              rows={4}
              required
              maxLength={10000}
              value={body}
              onChange={(e) => setBody(e.target.value)}
              error={error('body')}
              hint="Comments are plain text. Select a member below to add a mention."
            />
            <div className="work-mention-controls">
              <SelectField
                label="Mention a member"
                value={mention}
                disabled={!members.data}
                onChange={(e) => setMention(e.target.value)}
              >
                <option value="">Choose a member</option>
                {members.data?.map((member) => (
                  <option key={member.userId} value={member.userId}>
                    {member.displayName}
                  </option>
                ))}
              </SelectField>
              <Button
                type="button"
                variant="secondary"
                disabled={!mention}
                onClick={() => {
                  setBody(
                    (value) =>
                      `${value}${value && !/\s$/.test(value) ? ' ' : ''}@{${mention}} `,
                  )
                  setMention('')
                }}
              >
                Insert mention
              </Button>
            </div>
            {members.isError && (
              <p role="status" className="muted">
                Member suggestions are unavailable.{' '}
                <button
                  type="button"
                  className="text-link"
                  onClick={() => void members.refetch()}
                >
                  Retry members
                </button>
              </p>
            )}
          </>
        )}
      </WorkForm>
    </>
  )
}
export function Comments({
  scope,
  itemId,
  userId,
  role,
  writable,
}: {
  scope: Scope
  itemId: string
  userId: string
  role: Role
  writable: boolean
}) {
  const [page, setPage] = useState(1)
  const members = useMembers(scope)
  const comments = useQuery({
    queryKey: [...workKey(scope), 'comments', itemId, page],
    queryFn: ({ signal }) => workApi.comments(scope, itemId, page, signal),
    retry: false,
  })
  const name = (id: string) =>
    members.data?.find(
      (member) => member.userId.toLowerCase() === id.toLowerCase(),
    )?.displayName || `Previous or unavailable member (${id})`
  return (
    <section className="workspace-card" id="work-comments">
      <h2>Comments</h2>
      {writable && <CommentEditor scope={scope} itemId={itemId} />}
      <QueryState
        pending={comments.isPending}
        hasData={!!comments.data}
        error={comments.error}
        retry={comments.refetch}
      >
        {!comments.data?.items.length && <Empty>No comments yet.</Empty>}
        <ul className="workspace-rows work-comments">
          {comments.data?.items.map((comment) => (
            <li key={comment.id}>
              <article>
                <div className="workspace-section-heading">
                  <div>
                    <strong>{name(comment.authorUserId)}</strong>
                    <p className="muted">
                      <time dateTime={comment.createdAt}>
                        {timestamp(comment.createdAt)}
                      </time>
                      {comment.version > 1 ? ' · Edited' : ''}
                    </p>
                  </div>
                  {writable &&
                    canModerate(role, userId, comment.authorUserId) && (
                      <div className="workspace-actions">
                        <Modal title="Edit comment" trigger="Edit comment">
                          {(close) => (
                            <CommentEditor
                              scope={scope}
                              itemId={itemId}
                              comment={comment}
                              close={close}
                            />
                          )}
                        </Modal>
                        <WorkConfirm
                          scope={scope}
                          title="Delete comment?"
                          description="The comment will be removed from this thread. Its history is preserved in activity."
                          label="Delete comment"
                          run={() =>
                            workApi.deleteComment(scope, itemId, comment.id)
                          }
                        />
                      </div>
                    )}
                </div>
                <p className="work-comment-body">
                  {mentionParts(comment.body).map((part, index) =>
                    part.userId ? (
                      <span className="work-mention" key={index}>
                        @{name(part.userId)}
                      </span>
                    ) : (
                      part.text
                    ),
                  )}
                </p>
              </article>
            </li>
          ))}
        </ul>
        {comments.data && (
          <Pager page={page} total={comments.data.total} change={setPage} />
        )}
      </QueryState>
    </section>
  )
}
