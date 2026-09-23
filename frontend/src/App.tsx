import './App.css'
import { Route, Routes } from 'react-router'
import { Header } from './components/Header'
import { SessionExpiryWarning } from './components/SessionExpiryWarning'
import { DashboardPage } from './pages/DashboardPage'
import { HomePage } from './pages/HomePage'
import { NotFoundPage } from './pages/NotFoundPage'
import { ResetPasswordPage } from './pages/ResetPasswordPage'
import { SettingsPage } from './pages/SettingsPage'
import { VerifyEmailPage } from './pages/VerifyEmailPage'
import { RedirectIfSignedIn, RequireSignedIn } from './routing/guards'

/** Routes (spec 003, contracts/api-and-routes.md). */
export function AppRoutes() {
  return (
    <Routes>
      <Route
        path="/"
        element={
          <RedirectIfSignedIn>
            <HomePage />
          </RedirectIfSignedIn>
        }
      />
      <Route
        path="/dashboard"
        element={
          <RequireSignedIn>
            <DashboardPage />
          </RequireSignedIn>
        }
      />
      <Route
        path="/settings"
        element={
          <RequireSignedIn>
            <SettingsPage />
          </RequireSignedIn>
        }
      />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}

function App() {
  return (
    <div className="app">
      <Header />
      <main className="dashboard">
        <AppRoutes />
      </main>
      <SessionExpiryWarning />
    </div>
  )
}

export default App
