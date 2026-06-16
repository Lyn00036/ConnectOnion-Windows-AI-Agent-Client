/** Route table that maps desktop navigation paths to page components. */

import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { DesktopLayout } from '../layouts/DesktopLayout'
import { AgentsPage } from '../pages/AgentsPage'
import { ChatPage } from '../pages/ChatPage'
import { SessionsPage } from '../pages/SessionsPage'
import { SettingsPage } from '../pages/SettingsPage'

export function AppRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<DesktopLayout />}>
          <Route index element={<Navigate replace to="/chat" />} />
          <Route path="/chat" element={<ChatPage />} />
          <Route path="/agents" element={<AgentsPage />} />
          <Route path="/sessions" element={<SessionsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
