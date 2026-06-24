/** Route table that maps desktop navigation paths to page components. */

import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { DesktopLayout } from '../layouts/DesktopLayout'
import { ChatPage } from '../pages/ChatPage'
import { SettingsPage } from '../pages/SettingsPage'

export function AppRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<DesktopLayout />}>
          <Route index element={<Navigate replace to="/chat" />} />
          <Route path="/chat/:sessionId?" element={<ChatPage />} />
          <Route path="/agents" element={<Navigate replace to="/chat" />} />
          <Route path="/sessions" element={<Navigate replace to="/chat" />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
