import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <section className="card page-card">
      <h1>Page not found</h1>
      <Link className="primary-link" to="/">
        Go to Home
      </Link>
    </section>
  )
}
