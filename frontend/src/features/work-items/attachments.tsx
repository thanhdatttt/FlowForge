import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import {
  Empty,
  ErrorNotice,
  Pager,
  QueryState,
} from '@/features/workspaces/shared'
import type { Role } from '@/features/workspaces/api'
import { ApiError } from '@/lib/api'
import { workApi, type Attachment, type Scope } from './api'
import { canModerate, timestamp, validateFile } from './rules'
import { WorkConfirm, WorkForm } from './shared'
import { workKey } from './queries'

export function Attachments({
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
  const [uploadKey, setUploadKey] = useState(0)
  const [downloading, setDownloading] = useState<string | null>(null)
  const [downloadError, setDownloadError] = useState<unknown>(null)
  const attachments = useQuery({
    queryKey: [...workKey(scope), 'attachments', itemId, page],
    queryFn: ({ signal }) => workApi.attachments(scope, itemId, page, signal),
    retry: false,
  })
  async function download(file: Attachment) {
    if (downloading) return
    setDownloading(file.id)
    setDownloadError(null)
    try {
      const blob = await workApi.download(scope, itemId, file.id)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = file.originalName
      document.body.append(link)
      link.click()
      link.remove()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (error) {
      setDownloadError(error)
    } finally {
      setDownloading(null)
    }
  }
  return (
    <section className="workspace-card" id="work-files">
      <h2>Attachments</h2>
      {writable && (
        <WorkForm
          key={uploadKey}
          scope={scope}
          label="Upload file"
          done={() => {
            setUploadKey((key) => key + 1)
            setPage(1)
          }}
          submit={(data) => {
            const file = data.get('file')
            if (!(file instanceof File))
              throw new ApiError('Choose a file.', 400, {
                file: ['Choose a file.'],
              })
            const failure = validateFile(file)
            if (failure) throw new ApiError(failure, 400, { file: [failure] })
            return workApi.upload(scope, itemId, file)
          }}
        >
          {(error) => (
            <div className="work-upload-picker">
              <FormField
                label="Attach a file"
                name="file"
                type="file"
                required
                accept="image/png,image/jpeg,application/pdf,text/plain"
                hint="PNG, JPEG, PDF or TXT. Maximum 10 MiB per file."
                error={
                  error('file') || error('contentType') || error('fileName')
                }
              />
            </div>
          )}
        </WorkForm>
      )}
      <ErrorNotice error={downloadError} />
      <QueryState
        pending={attachments.isPending}
        hasData={!!attachments.data}
        error={attachments.error}
        retry={attachments.refetch}
      >
        {!attachments.data?.items.length && <Empty>No attachments yet.</Empty>}
        <ul className="workspace-rows">
          {attachments.data?.items.map((file) => (
            <li key={file.id}>
              <div className="work-entry-main">
                <strong>{file.originalName}</strong>
                <p className="muted">
                  {file.sizeBytes < 1024
                    ? `${file.sizeBytes} bytes`
                    : `${(file.sizeBytes / 1024).toLocaleString(undefined, { maximumFractionDigits: 1 })} KiB`}{' '}
                  · {timestamp(file.createdAt)}
                </p>
              </div>
              <div className="workspace-actions">
                <Button
                  variant="secondary"
                  disabled={!!downloading}
                  loading={downloading === file.id}
                  aria-label={`Download ${file.originalName}`}
                  onClick={() => void download(file)}
                >
                  Download
                </Button>
                {writable &&
                  canModerate(role, userId, file.uploadedByUserId) && (
                    <WorkConfirm
                      scope={scope}
                      title={`Delete ${file.originalName}?`}
                      description="The file will be removed from this work item and private storage."
                      label={`Delete ${file.originalName}`}
                      run={() =>
                        workApi.deleteAttachment(scope, itemId, file.id)
                      }
                    />
                  )}
              </div>
            </li>
          ))}
        </ul>
        {attachments.data && (
          <Pager page={page} total={attachments.data.total} change={setPage} />
        )}
      </QueryState>
    </section>
  )
}
