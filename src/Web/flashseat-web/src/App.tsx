import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { api, isAuthenticated, logout } from './api';
import { checkoutSessionEvent, clearCheckoutSession, readActiveCheckout, type ActiveCheckout } from './checkout-session';
import { AdminEventFormPage, AdminEventsPage } from './admin-pages';
import { AuthPage, BookingDetailPage, CheckInPage, CheckoutPage, EventDetailPage, HomePage, MyBookingsPage, SeatPage } from './pages';
import { FlashSeatLogo } from './logo';

function useCurrentUser() {
  return useQuery({ queryKey: ['current-user'], queryFn: api.me, enabled: isAuthenticated(), retry: false });
}

function Layout() {
  const nav = useNavigate();
  const location = useLocation();
  const qc = useQueryClient();
  const [authenticated, setAuthenticated] = useState(isAuthenticated());
  const [activeCheckout, setActiveCheckout] = useState<ActiveCheckout | null>(() => readActiveCheckout());
  const [theme, setTheme] = useState<'dark' | 'light'>(() => {
    const saved = localStorage.getItem('flashseat-theme');
    return saved === 'light' ? 'light' : 'dark';
  });
  const user = useCurrentUser();
  const activeHold = useQuery({
    queryKey: ['resume-hold', activeCheckout?.holdId],
    queryFn: () => api.hold(activeCheckout!.holdId),
    enabled: authenticated && !!activeCheckout,
    retry: false,
    refetchOnMount: 'always',
    refetchOnWindowFocus: true,
  });
  const activeBookingId = activeCheckout ? sessionStorage.getItem(`flashseat:booking-id:${activeCheckout.holdId}`) : null;
  const activePaymentId = activeCheckout ? sessionStorage.getItem(`flashseat:payment-id:${activeCheckout.holdId}`) : null;
  const activeBooking = useQuery({
    queryKey: ['resume-booking', activeBookingId],
    queryFn: () => api.booking(activeBookingId!),
    enabled: authenticated && !!activeBookingId,
    retry: false,
    refetchOnMount: 'always',
    refetchOnWindowFocus: true,
  });
  const activePayment = useQuery({
    queryKey: ['resume-payment', activePaymentId],
    queryFn: () => api.payment(activePaymentId!),
    enabled: authenticated && !!activePaymentId,
    retry: false,
    refetchOnMount: 'always',
    refetchOnWindowFocus: true,
  });
  const navClass = ({ isActive }: { isActive: boolean }) => isActive ? 'active' : undefined;
  const holdIsActive = !!activeHold.data && ['Active', 'Converted'].includes(activeHold.data.status) && Date.parse(activeHold.data.expiresAt) > Date.now();
  const bookingIsPending = activeBooking.data?.status === 'PendingPayment';
  const paymentIsPending = !activePaymentId || activePayment.data?.status === 'Pending';
  const paymentBelongsToBooking = !activePayment.data || activePayment.data.bookingId === activeBookingId;
  const resumeReady = authenticated && !!activeCheckout && !!activeBookingId && holdIsActive && bookingIsPending && paymentIsPending && paymentBelongsToBooking;

  useEffect(() => {
    if (!activeCheckout || activeHold.isLoading) return;
    if (activeHold.isError || !holdIsActive) {
      clearCheckoutSession(activeCheckout.holdId);
      return;
    }
    if (!activeBookingId || activeBooking.isLoading || activePayment.isLoading) return;
    const stale = activeBooking.isError || activePayment.isError || !bookingIsPending || !paymentIsPending || !paymentBelongsToBooking;
    if (stale) clearCheckoutSession(activeCheckout.holdId);
  }, [activeCheckout, activeBookingId, activeHold.isError, activeHold.isLoading, activeBooking.isError, activeBooking.isLoading, activePayment.isError, activePayment.isLoading, holdIsActive, bookingIsPending, paymentIsPending, paymentBelongsToBooking]);

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('flashseat-theme', theme);
  }, [theme]);

  const toggleTheme = () => {
    setTheme(current => current === 'dark' ? 'light' : 'dark');
  };

  useEffect(() => {
    const update = () => {
      const next = isAuthenticated();
      setAuthenticated(next);
      setActiveCheckout(next ? readActiveCheckout() : null);
      if (next) qc.invalidateQueries({ queryKey: ['current-user'] });
      else qc.clear();
    };
    window.addEventListener('auth-changed', update);
    return () => window.removeEventListener('auth-changed', update);
  }, [qc]);

  useEffect(() => {
    const update = () => setActiveCheckout(readActiveCheckout());
    window.addEventListener(checkoutSessionEvent, update);
    window.addEventListener('focus', update);
    return () => {
      window.removeEventListener(checkoutSessionEvent, update);
      window.removeEventListener('focus', update);
    };
  }, []);

  const resumePayment = !location.pathname.startsWith('/checkout/') && resumeReady && activeCheckout && <NavLink className="resume-payment" to={`/checkout/${activeCheckout.holdId}`}>Resume payment</NavLink>;

  return <>
    <a className="skip-link" href="#main-content">Skip to content</a>
    <header className="site-header">
      <div className="site-header-inner">
        <Link className="brand" to="/" aria-label="FlashSeat home">
          <FlashSeatLogo size={36} />
          <b>FlashSeat</b>
        </Link>
        <nav aria-label="Main navigation">
        <NavLink className={navClass} to="/" end>Events</NavLink>
        {user.data?.role === 'Admin' && <><NavLink className={navClass} to="/admin/events">Admin</NavLink><NavLink className={navClass} to="/admin/check-in">Check in</NavLink></>}
        {authenticated
          ? <>
              {resumePayment}
              {user.data && <NavLink className="user-nav-chip" to="/bookings" title={`${user.data.fullName} (${user.data.email})`}>
                <span className="user-nav-avatar">{user.data.fullName.slice(0, 1).toUpperCase()}</span>
                <span className="user-nav-name">{user.data.fullName.split(' ')[0]}</span>
              </NavLink>}
              <button className="ghost" onClick={() => { logout(); qc.removeQueries({ queryKey: ['current-user'] }); nav('/login'); }}>Sign out</button>
            </>
          : <NavLink className={({ isActive }) => `button small${isActive ? ' active' : ''}`} to="/login">Sign in</NavLink>}
        <button
          className="theme-toggle"
          onClick={toggleTheme}
          aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
          title={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
          aria-pressed={theme === 'light'}
        >
          <span className={`theme-icon ${theme === 'dark' ? 'theme-icon-sun' : 'theme-icon-moon'}`} aria-hidden="true" />
          <span className="sr-only">{theme === 'dark' ? 'Light' : 'Dark'} mode</span>
        </button>
        </nav>
      </div>
    </header>
    <main id="main-content"><Outlet /></main>
    <footer>
      <strong>FlashSeat</strong>
      <span>Tickets, seats, done.</span>
    </footer>
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
        <Route path="bookings/:id" element={<BookingDetailPage />} />
      </Route>
      <Route element={<AuthenticatedRoute role="Admin" />}>
        <Route path="admin/events" element={<AdminEventsPage />} />
        <Route path="admin/events/new" element={<AdminEventFormPage />} />
        <Route path="admin/events/:id/edit" element={<AdminEventFormPage />} />
        <Route path="admin/check-in" element={<CheckInPage />} />
      </Route>
      <Route path="*" element={<section className="center"><p className="kicker">404 / NOT FOUND</p><h1>This ticket leads nowhere.</h1><Link className="button" to="/">Browse events</Link></section>} />
    </Route>
  </Routes>;
}
