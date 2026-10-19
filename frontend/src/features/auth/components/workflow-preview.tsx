import {
  ArrowDown,
  ArrowRight,
  Check,
  CheckCheck,
  Circle,
  CircleCheck,
  Ellipsis,
  Flag,
  Layers2,
  MessageSquare,
  Zap,
} from 'lucide-react'

const columns = [
  {
    name: 'To do',
    tone: 'todo',
    cards: [
      {
        id: 'WEB-24',
        title: 'Map the onboarding journey',
        label: 'Research',
        tone: 'purple',
        initials: 'AL',
        comments: 2,
      },
      {
        id: 'WEB-28',
        title: 'Write launch announcement',
        label: 'Content',
        tone: 'blue',
        initials: 'JM',
        comments: 3,
      },
    ],
  },
  {
    name: 'In progress',
    tone: 'progress',
    cards: [
      {
        id: 'WEB-21',
        title: 'Build the component library',
        label: 'Design system',
        tone: 'purple',
        initials: 'SK',
        comments: 4,
      },
      {
        id: 'WEB-26',
        title: 'Connect the feedback form',
        label: 'Development',
        tone: 'blue',
        initials: 'AL',
        comments: 1,
      },
    ],
  },
  {
    name: 'Done',
    tone: 'done',
    cards: [
      {
        id: 'WEB-18',
        title: 'Set the visual direction',
        label: 'Design',
        tone: 'green',
        initials: 'SK',
        comments: 6,
      },
    ],
  },
]

export function WorkflowPreview() {
  return (
    <figure
      className="workflow-preview"
      aria-label="Example project board with an automation that notifies members when a task is completed"
    >
      <div className="preview-board" aria-hidden="true">
        <div className="board-top">
          <div className="project-icon">
            <Layers2 size={17} />
          </div>
          <strong>Website launch</strong>
          <span className="preview-label">PROJECT PREVIEW</span>
          <Ellipsis size={18} />
        </div>
        <div className="board-toolbar">
          <span className="board-tab">
            Board <span>5</span>
          </span>
          <span>Overview</span>
          <span>Activity</span>
          <div className="avatar-stack">
            <i>AL</i>
            <i>SK</i>
            <i>JM</i>
          </div>
        </div>
        <div className="board-columns">
          {columns.map((column) => (
            <div className="board-column" key={column.name}>
              <div className={`column-heading ${column.tone}`}>
                {column.tone === 'done' ? (
                  <CircleCheck size={12} />
                ) : (
                  <Circle size={12} />
                )}
                <span>{column.name}</span>
                <small>{column.cards.length}</small>
                <span className="column-plus">+</span>
              </div>
              {column.cards.map((card) => (
                <div
                  className={`task-card ${column.tone === 'done' ? 'task-complete' : ''}`}
                  key={card.id}
                >
                  <span className="task-key">
                    {card.id}
                    <Ellipsis size={12} />
                  </span>
                  <p>{card.title}</p>
                  <span className={`task-label ${card.tone}`}>
                    {card.label}
                  </span>
                  <div className="task-meta">
                    {column.tone === 'done' ? (
                      <CheckCheck size={13} className="done-icon" />
                    ) : (
                      <Flag size={12} />
                    )}
                    <span>
                      <MessageSquare size={11} />
                      {card.comments}
                    </span>
                    <i
                      className={`avatar avatar-${card.initials.toLowerCase()}`}
                    >
                      {card.initials}
                    </i>
                  </div>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>
      <div className="automation-connector" aria-hidden="true">
        <span />
        <ArrowDown size={17} />
      </div>
      <div className="automation-card" aria-hidden="true">
        <div className="automation-heading">
          <span className="automation-icon">
            <Zap size={17} fill="currentColor" />
          </span>
          <strong>Let your workflow do the work</strong>
          <span className="active-badge">
            <span />
            Active
          </span>
        </div>
        <div className="automation-flow">
          <div>
            <span>WHEN</span>
            <p>
              Status changes to{' '}
              <b>
                <Check size={11} />
                Done
              </b>
            </p>
          </div>
          <ArrowRight size={17} />
          <div>
            <span>THEN</span>
            <p>Notify project members</p>
          </div>
        </div>
      </div>
      <figcaption>
        <span className="caption-line" />
        Less busywork. More forward momentum.
      </figcaption>
    </figure>
  )
}
