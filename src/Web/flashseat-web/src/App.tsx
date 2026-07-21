import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { api, isAuthenticated, logout } from './api';
import { AdminEventFormPage, AdminEventsPage } from './admin-pages';
import { AuthPage, CheckoutPage, EventDetailPage, HomePage, MyBookingsPage, SeatPage } from './pages';

function useCurrentUser() {
  return useQuery({ queryKey: ['current-user'], queryFn: api.me, enabled: isAuthenticated(), retry: false });
}

function Layout() {
  const nav = useNavigate();
  const qc = useQueryClient();
  const [authenticated, setAuthenticated] = useState(isAuthenticated());
  const user = useCurrentUser();
  const navClass = ({ isActive }: { isActive: boolean }) => isActive ? 'active' : undefined;

  useEffect(() => {
    const update = () => {
      const next = isAuthenticated();
      setAuthenticated(next);
      if (next) qc.invalidateQueries({ queryKey: ['current-user'] });
      else qc.clear();
    };
    window.addEventListener('auth-changed', update);
    return () => window.removeEventListener('auth-changed', update);
  }, [qc]);

  return <>
    <a className="skip-link" href="#main-content">Skip to content</a>
    <header>
      <Link className="brand" to="/" aria-label="FlashSeat home"><span>FS</span><b>FlashSeat</b></Link>
      <nav aria-label="Main navigation">
        <NavLink className={navClass} to="/" end>Events</NavLink>
        {user.data && <NavLink className={navClass} to="/bookings">My tickets</NavLink>}
        {user.data?.role === 'Admin' && <NavLink className={navClass} to="/admin/events">Admin</NavLink>}
        {authenticated
          ? <button className="ghost" onClick={() => { logout(); qc.removeQueries({ queryKey: ['current-user'] }); nav('/login'); }}>Sign out</button>
          : <NavLink className={({ isActive }) => `button small${isActive ? ' active' : ''}`} to="/login">Sign in</NavLink>}
      </nav>
    </header>
    <main id="main-content"><Outlet /></main>
    <footer><strong>FlashSeat</strong><span>Tickets, seats, done.</span></footer>
  </>;
}

function AuthenticatedRoute({ role }: { role?: 'Admin' }) {
  const location = useLocation();
  const user = useCurrentUser();
  if (!isAuthenticated()) return <Navigate to="/login" state={{ from: `${location.pathname}${location.search}` }} replace />;
  if (user.isLoading) return <div className="skeleton" role="status"><span className="sr-only">Loading account</span></div>;
  if (user.isError) return <div className="empty" role="alert"><p>We couldn't verify your account.</p><button className="ghost" onClick={() => user.refetch()}>Try again</button></div>;
  if (role && user.data?.role !== role) return <Navigate to="/" replace />;
  return <Outlet />;
}

export default function App() {
  return <Routes>
    <Route element={<Layout />}>
      <Route index element={<HomePage />} />
      <Route path="events/:id" element={<EventDetailPage />} />
      <Route path="login" element={<AuthPage />} />
      <Route element={<AuthenticatedRoute />}>
        <Route path="events/:id/seats" element={<SeatPage />} />
        <Route path="checkout/:holdId" element={<CheckoutPage />} />
        <Route path="bookings" element={<MyBookingsPage />} />
      </Route>
      <Route element={<AuthenticatedRoute role="Admin" />}>
        <Route path="admin/events" element={<AdminEventsPage />} />
        <Route path="admin/events/new" element={<AdminEventFormPage />} />
        <Route path="admin/events/:id/edit" element={<AdminEventFormPage />} />
      </Route>
      <Route path="*" element={<section className="center"><p className="kicker">404 / NOT FOUND</p><h1>This ticket leads nowhere.</h1><Link className="button" to="/">Browse events</Link></section>} />
    </Route>
  </Routes>;
}
