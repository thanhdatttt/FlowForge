import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { FormField } from '@/components/ui/form-field'
import { QueryState, SelectField } from '@/features/workspaces/shared'
import { text } from '@/features/workspaces/rules'
import { workApi, type Scope, type WorkItem, type WorkItemInput } from './api'
import { itemTypes, parentChoices, priorities } from './rules'
import { TextArea, WorkForm } from './shared'
import { useLabels, useMembers, useRelations, workKey } from './queries'

export function ItemEditor({
  scope,
  item: current,
  parentId,
  saved,
}: {
  scope: Scope
  item?: WorkItem
  parentId?: string
  saved: (item: WorkItem) => void
}) {
  // Keep the edit snapshot and its version together, even across background refetches.
  const [item] = useState(current)
  const [conflict, setConflict] = useState(false)
  const [start, setStart] = useState(item?.startDate || '')
  const statuses = useQuery({
    queryKey: [...workKey(scope), 'statuses'],
    queryFn: ({ signal }) => workApi.statuses(scope, signal),
    retry: false,
  })
  const labels = useLabels(scope)
  const members = useMembers(scope)
  const relations = useRelations(scope)
  const queries = [statuses, labels, members, relations]
  const error = queries.find((q) => q.isError)?.error
  const pending = queries.some((q) => q.isPending)
  const parents = parentChoices(relations.data || [], item?.id)
  const selectedParent = item?.parentId || parentId || ''
  return (
    <QueryState
      pending={pending}
      hasData={queries.every(query => query.data !== undefined)}
      error={error}
      retry={() => queries.forEach((q) => void q.refetch())}
    >
      {conflict && (
        <p className="error-notice" role="alert">
          This item changed or became read only. Your draft is kept below. Copy
          any changes you need, then close and reopen the editor to load its
          latest version.
        </p>
      )}
      <WorkForm
        scope={scope}
        disabled={conflict}
        label={item ? 'Save work item' : 'Create work item'}
        onConflict={() => setConflict(true)}
        submit={async (data) => {
          const estimate = text(data, 'estimateMinutes')
          const input: WorkItemInput = {
            title: text(data, 'title'),
            description: text(data, 'description') || null,
            type: text(data, 'type') as WorkItemInput['type'],
            status: text(data, 'status'),
            priority: text(data, 'priority') as WorkItemInput['priority'],
            assigneeUserId: text(data, 'assigneeUserId') || null,
            parentId: text(data, 'parentId') || null,
            startDate: text(data, 'startDate') || null,
            dueDate: text(data, 'dueDate') || null,
            estimateMinutes: estimate === '' ? null : Number(estimate),
            labelIds: data.getAll('labelIds').map(String),
          }
          const result = item
            ? await workApi.update(scope, item.id, input, item.version)
            : await workApi.create(scope, input)
          saved(result)
        }}
      >
        {(error) => (
          <>
            <FormField
              label="Title"
              name="title"
              required
              maxLength={200}
              pattern=".*\S.*"
              defaultValue={item?.title || ''}
              error={error('title')}
            />
            <TextArea
              label="Description"
              name="description"
              rows={4}
              maxLength={20000}
              defaultValue={item?.description || ''}
              error={error('description')}
            />
            <div className="workspace-columns">
              <SelectField
                label="Type"
                name="type"
                defaultValue={item?.type || 'Task'}
                error={error('type')}
              >
                {itemTypes.map((type) => (
                  <option key={type}>{type}</option>
                ))}
              </SelectField>
              <SelectField
                label="Priority"
                name="priority"
                defaultValue={item?.priority || 'Medium'}
                error={error('priority')}
              >
                {priorities.map((priority) => (
                  <option key={priority}>{priority}</option>
                ))}
              </SelectField>
            </div>
            <SelectField
              label="Status"
              name="status"
              required
              defaultValue={item?.status || statuses.data?.[0]?.name}
              error={error('status')}
            >
              {item && !statuses.data?.some((s) => s.name === item.status) && (
                <option value={item.status}>{item.status} (unavailable)</option>
              )}
              {statuses.data?.map((status) => (
                <option key={status.id} value={status.name}>
                  {status.name.replace(/([a-z])([A-Z])/g, '$1 $2')}
                </option>
              ))}
            </SelectField>
            <SelectField
              label="Assignee"
              name="assigneeUserId"
              defaultValue={item?.assigneeUserId || ''}
              error={error('assigneeUserId')}
            >
              <option value="">Unassigned</option>
              {item?.assigneeUserId &&
                !members.data?.some(
                  (m) => m.userId === item.assigneeUserId,
                ) && (
                  <option value={item.assigneeUserId}>
                    Previous assignee · choose a current member or unassign
                  </option>
                )}
              {members.data?.map((member) => (
                <option key={member.userId} value={member.userId}>
                  {member.displayName} · {member.role}
                </option>
              ))}
            </SelectField>
            <SelectField
              label="Parent work item"
              name="parentId"
              defaultValue={selectedParent}
              error={error('parentId')}
            >
              <option value="">No parent</option>
              {selectedParent &&
                !parents.some((p) => p.id === selectedParent) && (
                  <option value={selectedParent}>
                    Previous parent · choose an active item or remove
                  </option>
                )}
              {parents.map((parent) => (
                <option key={parent.id} value={parent.id}>
                  {parent.key} · {parent.title}
                </option>
              ))}
            </SelectField>
            <div className="workspace-columns">
              <FormField
                label="Start date"
                name="startDate"
                type="date"
                value={start}
                onChange={(event) => setStart(event.target.value)}
                error={error('startDate')}
              />
              <FormField
                label="Due date"
                name="dueDate"
                type="date"
                min={start || undefined}
                defaultValue={item?.dueDate || ''}
                error={error('dueDate')}
              />
            </div>
            <FormField
              label="Estimate (minutes)"
              name="estimateMinutes"
              type="number"
              min={0}
              step={1}
              defaultValue={item?.estimateMinutes ?? ''}
              error={error('estimateMinutes')}
            />
            <fieldset className="work-label-picker">
              <legend>
                Labels <span className="muted">· up to 20</span>
              </legend>
              {!labels.data?.length && (
                <p className="muted">
                  No labels yet. Add them from the project’s Labels page.
                </p>
              )}
              {labels.data?.map((label) => (
                <label key={label.id}>
                  <input
                    type="checkbox"
                    name="labelIds"
                    value={label.id}
                    defaultChecked={item?.labelIds.includes(label.id)}
                  />
                  {label.name}
                </label>
              ))}
              {error('labelIds') && (
                <p className="field-error" role="alert">
                  {error('labelIds')}
                </p>
              )}
            </fieldset>
          </>
        )}
      </WorkForm>
    </QueryState>
  )
}
