export function LogoMark({ size = 34 }: { size?: number }) {
  return (
    <img
      className="brand-mark"
      src="/logo.svg"
      width={size}
      height={size}
      alt=""
      aria-hidden="true"
    />
  )
}

export function Brand() {
  return (
    <div className="brand" aria-label="FlowForge">
      <LogoMark />
      <span>
        Flow<span className="brand-period">Forge</span>
      </span>
    </div>
  )
}
