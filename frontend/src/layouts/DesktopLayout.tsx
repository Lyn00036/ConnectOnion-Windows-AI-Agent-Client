/** Shared desktop shell with the top bar, navigation, and page outlet. */

import { NavLink, Outlet } from 'react-router-dom'

export function DesktopLayout() {
  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <h1>Agent Console</h1>
          <p>Connect to any agent service that exposes /health and /chat</p>
        </div>
        <nav className="layout-nav" aria-label="Primary">
          <NavLink to="/chat">Chat</NavLink>
          <NavLink to="/agents">Agents</NavLink>
          <NavLink to="/sessions">Sessions</NavLink>
          <NavLink to="/settings">Settings</NavLink>
        </nav>
      </header>

      <Outlet />
    </main>
  )
}
