import { useState } from 'react'
import { FormField } from '@/components/ui/form-field'
import { Empty, Modal, QueryState } from '@/features/workspaces/shared'
import type { Project, Workspace } from '@/features/workspaces/api'
import { text } from '@/features/workspaces/rules'
import { workApi, type Label, type Scope } from './api'
import { canWriteWork } from './rules'
import { WorkConfirm, WorkForm } from './shared'
import { useLabels } from './queries'

function LabelEditor({
  scope,
  label: current,
  close,
}: {
  scope: Scope
  label?: Label
  close: () => void
}) {
  const [label] = useState(current)
  return (
    <WorkForm
      scope={scope}
      done={close}
      label={label ? 'Save label' : 'Create label'}
      submit={(data) =>
        workApi.saveLabel(
          scope,
          { name: text(data, 'name'), color: text(data, 'color') || null },
          label?.id,
        )
      }
    >
      {(error) => (
        <>
          <FormField
            label="Label name"
            name="name"
            required
            pattern=".*\S.*"
            maxLength={50}
            defaultValue={label?.name || ''}
            error={error('name')}
          />
          <FormField
            label="Color (optional)"
            name="color"
            placeholder="#5146d6"
            pattern="#[0-9a-fA-F]{6}"
            defaultValue={label?.color || ''}
            hint="Use a six-digit hex color, or leave empty."
            error={error('color')}
          />
        </>
      )}
    </WorkForm>
  )
}
export function Labels({
  workspace,
  project,
}: {
  workspace: Workspace
  project: Project
}) {
  const scope = { workspaceId: workspace.id, projectId: project.id }
  const labels = useLabels(scope)
  const writable = canWriteWork(workspace.role, !!project.archivedAt)
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Labels</h2>
          <p className="muted">Organize the work in this project.</p>
        </div>
        {writable && (
          <Modal title="Create label" trigger="New label">
            {(close) => <LabelEditor scope={scope} close={close} />}
          </Modal>
        )}
      </div>
      <QueryState
        pending={labels.isPending}
        hasData={!!labels.data}
        error={labels.error}
        retry={labels.refetch}
      >
        {!labels.data?.length && <Empty>No labels yet.</Empty>}
        <ul className="workspace-rows">
          {labels.data?.map((label) => (
            <li key={label.id}>
              <span className="work-label">
                {label.color && (
                  <span
                    className="work-color"
                    style={{ background: label.color }}
                    aria-hidden="true"
                  />
                )}
                {label.name}
              </span>
              {writable && (
                <div className="workspace-actions">
                  <Modal
                    title={`Edit ${label.name}`}
                    trigger={`Edit ${label.name}`}
                  >
                    {(close) => (
                      <LabelEditor scope={scope} label={label} close={close} />
                    )}
                  </Modal>
                  <WorkConfirm
                    scope={scope}
                    title={`Delete ${label.name}?`}
                    description="This removes the label from every work item in this project. Their history is preserved."
                    label={`Delete ${label.name}`}
                    run={() => workApi.deleteLabel(scope, label.id)}
                  />
                </div>
              )}
            </li>
          ))}
        </ul>
      </QueryState>
    </section>
  )
}
