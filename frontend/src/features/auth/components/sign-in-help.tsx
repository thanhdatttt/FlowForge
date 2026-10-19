import { Dialog } from '@base-ui/react/dialog'
import { X } from 'lucide-react'

export function SignInHelp() {
  return (
    <Dialog.Root>
      <Dialog.Trigger className="text-link">Trouble signing in?</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Backdrop className="dialog-backdrop" />
        <Dialog.Popup className="dialog-popup">
          <Dialog.Title>Let’s get you signed in</Dialog.Title>
          <Dialog.Description>
            Use the email address you registered with and check that Caps Lock
            is off when entering your password.
          </Dialog.Description>
          <p>
            Password reset is not available yet. If you’ve forgotten your
            password, contact your FlowForge administrator for help.
          </p>
          <Dialog.Close
            className="dialog-close icon-button"
            aria-label="Close sign-in help"
          >
            <X size={19} />
          </Dialog.Close>
          <Dialog.Close className="button button--primary">
            Back to sign in
          </Dialog.Close>
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
