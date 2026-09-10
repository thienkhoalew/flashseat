import { type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminEventFormPage, AdminEventsPage } from './admin-pages';
import { appendRowWithSeats, DEFAULT_STAGE_POSITION, emptySeatLayout, layoutFromSeats, placeStage, removeRow, renumberLayout, rotateRow, serializeSeatLayout, SeatLayoutEditor, stagePosition, translateRow, translateStage } from './seat-layout-editor';
import { ApiError, api, logout, saveAuth } from './api';
import { setActiveCheckout } from './checkout-session';
import App from './App';
import { AuthPage, BookingDetailPage, CheckInPage, CheckoutPage, EventDetailPage, HomePage, MyBookingsPage, SeatPage } from './pages';

vi.mock('@microsoft/signalr',()=>({HubConnectionBuilder:class{
  withUrl(){return this;} withAutomaticReconnect(){return this;}
  build(){return {on:vi.fn(),onreconnected:vi.fn(),start:vi.fn().mockResolvedValue(undefined),invoke:vi.fn().mockResolvedValue(undefined),stop:vi.fn().mockResolvedValue(undefined)};}
}}));

const renderWithQuery=(node:ReactNode,router=true)=>{const queryClient=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});return render(<QueryClientProvider client={queryClient}>{router?<BrowserRouter>{node}</BrowserRouter>:node}</QueryClientProvider>)};
afterEach(()=>{cleanup();vi.useRealTimers();vi.restoreAllMocks();sessionStorage.clear();logout();});

describe('AuthPage',()=>{
  it('renders English labeled login fields',()=>{
    renderWithQuery(<AuthPage/>);
    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByRole('button',{name:'Sign in'})).toBeInTheDocument();
  });

  it('fills demo credentials without signing in',()=>{
    const login=vi.spyOn(api,'login');
    renderWithQuery(<AuthPage/>);
    fireEvent.click(screen.getByRole('button',{name:/Customer demo@flashseat.dev/}));
    expect(screen.getByLabelText('Email')).toHaveValue('demo@flashseat.dev');
    expect(screen.getByLabelText('Password')).toHaveValue('Demo@123456');
    fireEvent.click(screen.getByRole('button',{name:/Admin admin@flashseat.dev/}));
    expect(screen.getByLabelText('Email')).toHaveValue('admin@flashseat.dev');
    expect(screen.getByLabelText('Password')).toHaveValue('Admin@123456');
    expect(login).not.toHaveBeenCalled();
  });

  it('requires a full name when registering',async()=>{
    renderWithQuery(<AuthPage/>);
    fireEvent.click(screen.getByRole('button',{name:'Need an account? Register'}));
    fireEvent.change(screen.getByLabelText('Email'),{target:{value:'person@example.com'}});
    fireEvent.change(screen.getByLabelText('Password'),{target:{value:'StrongPass1!'}});
    fireEvent.click(screen.getByRole('button',{name:'Create account'}));
    const error=await screen.findByText('Full name must contain at least 2 characters');
    expect(error).toBeInTheDocument();
    expect(screen.getByLabelText('Full name')).toHaveAttribute('aria-describedby',error.id);
  });

  it('renders Google sign in button and logs in on callback',async()=>{
    let gisCallback: ((res: { credential: string }) => void) | null = null;
    const initialize = vi.fn().mockImplementation((config: { callback: (res: { credential: string }) => void }) => {
      gisCallback = config.callback;
    });
    const renderButton = vi.fn();
    window.google = {
      accounts: {
        id: {
          initialize,
          renderButton,
        },
      },
    };

    const googleAuth = vi.spyOn(api, 'googleAuth').mockResolvedValue({
      accessToken: 'test-google-access-token',
      accessTokenExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'test-google-refresh-token',
      refreshTokenExpiresAt: '2099-01-08T00:00:00Z',
    });

    renderWithQuery(<AuthPage/>);
    expect(screen.getByTestId('google-signin-btn')).toBeInTheDocument();
    expect(initialize).toHaveBeenCalledWith(expect.objectContaining({
      client_id: expect.any(String),
    }));
    expect(renderButton).toHaveBeenCalled();

    expect(gisCallback).not.toBeNull();
    gisCallback!({ credential: 'mock-google-id-token' });

    await waitFor(() => expect(googleAuth).toHaveBeenCalledWith('mock-google-id-token'));
    expect(localStorage.getItem('accessToken')).toBe('test-google-access-token');
    delete window.google;
  });

  it('shows verify OTP screen after registration and allows verifying',async()=>{
    const registerSpy = vi.spyOn(api, 'register').mockResolvedValue({
      email: 'newuser@example.com',
      message: 'Verification code sent to your email.',
    });
    const verifySpy = vi.spyOn(api, 'verifyEmail').mockResolvedValue({
      accessToken: 'verified-token',
      accessTokenExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'verified-refresh',
      refreshTokenExpiresAt: '2099-01-08T00:00:00Z',
    });

    renderWithQuery(<AuthPage/>);
    fireEvent.click(screen.getByRole('button',{name:'Need an account? Register'}));
    fireEvent.change(screen.getByLabelText('Full name'),{target:{value:'New User'}});
    fireEvent.change(screen.getByLabelText('Email'),{target:{value:'newuser@example.com'}});
    fireEvent.change(screen.getByLabelText('Password'),{target:{value:'StrongPass1!'}});
    fireEvent.click(screen.getByRole('button',{name:'Create account'}));

    await waitFor(()=>expect(registerSpy).toHaveBeenCalledWith('newuser@example.com','StrongPass1!','New User'));
    expect(await screen.findByRole('heading',{name:'Check your email'})).toBeInTheDocument();
    expect(screen.getByText('newuser@example.com')).toBeInTheDocument();

    const codeInput = screen.getByLabelText('Verification code');
    fireEvent.change(codeInput, { target: { value: '123456' } });
    fireEvent.click(screen.getByRole('button',{name:'Verify email'}));

    await waitFor(()=>expect(verifySpy).toHaveBeenCalledWith('newuser@example.com','123456'));
    expect(localStorage.getItem('accessToken')).toBe('verified-token');
  });

  it('transitions to verify screen when login fails with unverified email',async()=>{
    vi.spyOn(api, 'login').mockRejectedValue(new ApiError(403, {
      title: 'Email not verified.',
      code: 'EMAIL_NOT_VERIFIED',
      unavailableSeatIds: [],
    }));

    renderWithQuery(<AuthPage/>);
    fireEvent.change(screen.getByLabelText('Email'),{target:{value:'unverified@example.com'}});
    fireEvent.change(screen.getByLabelText('Password'),{target:{value:'Password123!'}});
    fireEvent.click(screen.getByRole('button',{name:'Sign in'}));

    expect(await screen.findByRole('heading',{name:'Check your email'})).toBeInTheDocument();
    expect(screen.getByText('unverified@example.com')).toBeInTheDocument();
  });

  it('resends verification code on button click',async()=>{
    vi.spyOn(api, 'register').mockResolvedValue({
      email: 'resend@example.com',
      message: 'Code sent',
    });
    const resendSpy = vi.spyOn(api, 'resendVerification').mockResolvedValue({
      message: 'If the account exists and is unverified, a new code has been sent.',
    });

    renderWithQuery(<AuthPage/>);
    fireEvent.click(screen.getByRole('button',{name:'Need an account? Register'}));
    fireEvent.change(screen.getByLabelText('Full name'),{target:{value:'Resend User'}});
    fireEvent.change(screen.getByLabelText('Email'),{target:{value:'resend@example.com'}});
    fireEvent.change(screen.getByLabelText('Password'),{target:{value:'StrongPass1!'}});
    fireEvent.click(screen.getByRole('button',{name:'Create account'}));

    expect(await screen.findByRole('heading',{name:'Check your email'})).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button',{name:'Resend code'}));

    await waitFor(()=>expect(resendSpy).toHaveBeenCalledWith('resend@example.com'));
    expect(await screen.findByText('A new verification code has been sent to your email.')).toBeInTheDocument();
  });
});

describe('HomePage',()=>{
  it('paginates and resets to page one when searching',async()=>{
    const events=vi.spyOn(api,'events').mockImplementation(async(search,page=1)=>({items:[{id:`event-${page}`,name:search||`Event ${page}`,slug:'event',imageUrl:'https://example.com/event.jpg',venueName:'Venue',startsAt:'2027-09-01T12:00:00Z',endsAt:'2027-09-01T14:00:00Z',salesStartAt:'2027-08-01T12:00:00Z',salesEndAt:'2027-09-01T11:00:00Z',minPrice:100,currency:'USD',status:'Published'}],page,pageSize:12,totalCount:24}));
    renderWithQuery(<HomePage/>);
    await screen.findByText('Event 1');
    fireEvent.click(screen.getByRole('button',{name:'Next'}));
    await waitFor(()=>expect(events).toHaveBeenLastCalledWith('',2));
    fireEvent.change(screen.getByLabelText('Search the listings'),{target:{value:'Jazz'}});
    await waitFor(()=>expect(events).toHaveBeenLastCalledWith('Jazz',1));
  });

  it('renders upcoming events sorted by nearest start time in a one-card carousel',async()=>{
    vi.spyOn(api,'events').mockResolvedValue({items:[
      {id:'event-2',name:'Later Show',slug:'later-show',imageUrl:'https://example.com/later.jpg',venueName:'Venue Two',startsAt:'2027-08-25T12:00:00Z',endsAt:'2027-08-25T14:00:00Z',salesStartAt:'2027-08-18T10:00:00Z',salesEndAt:'2027-08-25T11:00:00Z',minPrice:120,currency:'USD',status:'Published'},
      {id:'event-1',name:'Earlier Show',slug:'earlier-show',imageUrl:'https://example.com/earlier.jpg',venueName:'Venue One',startsAt:'2027-08-20T12:00:00Z',endsAt:'2027-08-20T14:00:00Z',salesStartAt:'2027-08-18T10:00:00Z',salesEndAt:'2027-08-20T11:00:00Z',minPrice:100,currency:'USD',status:'Published'},
    ],page:1,pageSize:12,totalCount:2});
    renderWithQuery(<HomePage/>);
    const feed = await screen.findByRole('list',{name:'Upcoming event cards'});
    expect(feed).toHaveAttribute('id','event-feed');
    const links = screen.getAllByRole('link',{name:/^View /});
    expect(links).toHaveLength(2);
    expect(links[0]).toHaveAttribute('aria-label','View Earlier Show');
    expect(links[1]).toHaveAttribute('aria-label','View Later Show');
    expect(links[0]).toHaveAttribute('aria-current','true');
    expect(links[1]).not.toHaveAttribute('aria-current');

    const prevButton = screen.getByRole('button',{name:'Previous event'});
    const nextButton = screen.getByRole('button',{name:'Next event'});
    expect(prevButton).toBeDisabled();
    expect(nextButton).toBeEnabled();

    fireEvent.click(nextButton);
    expect(links[0]).not.toHaveAttribute('aria-current');
    expect(links[1]).toHaveAttribute('aria-current','true');
    expect(prevButton).toBeEnabled();
    expect(nextButton).toBeDisabled();

    fireEvent.click(prevButton);
    expect(links[0]).toHaveAttribute('aria-current','true');
    expect(links[1]).not.toHaveAttribute('aria-current');
    expect(prevButton).toBeDisabled();
  });

  it('reveals the event listing when clicking hero or explore button',async()=>{
    const scrollTo = vi.fn();
    Object.defineProperty(window,'scrollTo',{value:scrollTo,configurable:true});
    vi.spyOn(api,'events').mockResolvedValue({items:[],page:1,pageSize:12,totalCount:0});
    renderWithQuery(<HomePage/>);

    const listing = document.querySelector('.listing-section');
    expect(listing).not.toBeNull();
    Object.defineProperty(listing, 'getBoundingClientRect', { value: () => ({ top: 1000 }), configurable: true });
    fireEvent.click(screen.getByRole('button',{name:/Explore events/}));
    expect(scrollTo).toHaveBeenCalledWith({top:1000,behavior:'smooth'});

    fireEvent.wheel(window, { deltaY: 100 });
    expect(scrollTo).toHaveBeenCalledTimes(2);
  });

  it('opens the event detail page from the event name',async()=>{
    vi.spyOn(api,'events').mockResolvedValue({items:[{id:'event-1',name:'Morning Show',slug:'morning-show',imageUrl:'https://example.com/event.jpg',venueName:'Venue',startsAt:'2027-08-20T12:00:00Z',endsAt:'2027-08-20T14:00:00Z',salesStartAt:'2027-08-18T10:00:00Z',salesEndAt:'2027-08-20T11:00:00Z',minPrice:100,currency:'USD',status:'Published'}],page:1,pageSize:12,totalCount:1});
    renderWithQuery(<MemoryRouter initialEntries={['/']}><Routes><Route path="/" element={<HomePage/>}/><Route path="/events/:id" element={<p>Event detail reached</p>}/></Routes></MemoryRouter>,false);
    const eventCard = await screen.findByRole('link',{name:'View Morning Show'});
    expect(screen.queryByText('View event')).not.toBeInTheDocument();
    fireEvent.click(eventCard);
    expect(await screen.findByText('Event detail reached')).toBeInTheDocument();
  });

  it('keeps sold-out events visible and gives Sold out precedence',async()=>{
    vi.spyOn(api,'events').mockResolvedValue({items:[{id:'event-1',name:'Sold Show',slug:'sold-show',imageUrl:'https://example.com/event.jpg',venueName:'Venue',startsAt:'2027-08-20T12:00:00Z',endsAt:'2027-08-20T14:00:00Z',salesStartAt:'2027-08-18T10:00:00Z',salesEndAt:'2027-08-20T11:00:00Z',minPrice:100,currency:'USD',status:'Published',availabilityStatus:'SoldOut',availableSeatCount:0,totalSeatCount:1}],page:1,pageSize:12,totalCount:1});
    renderWithQuery(<HomePage/>);
    expect(await screen.findByText('Sold Show')).toBeInTheDocument();
    expect(screen.getByText('Sold out')).toBeInTheDocument();
    expect(screen.queryByText('On sale')).not.toBeInTheDocument();
  });

  it('updates the purchase action when sales open',async()=>{
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-08-18T09:59:59Z'));
    vi.spyOn(api,'events').mockResolvedValue({items:[{id:'event-1',name:'Morning Show',slug:'morning-show',imageUrl:'https://example.com/event.jpg',venueName:'Venue',startsAt:'2026-08-20T12:00:00Z',endsAt:'2026-08-20T14:00:00Z',salesStartAt:'2026-08-18T10:00:00Z',salesEndAt:'2026-08-20T11:00:00Z',minPrice:100,currency:'USD',status:'Published'}],page:1,pageSize:12,totalCount:1});
    renderWithQuery(<HomePage/>);

    await vi.waitFor(()=>expect(screen.getByText('Tickets open in 00.00.01')).toBeInTheDocument());
    const eventCard = screen.getByRole('link',{name:'View Morning Show'});
    expect(eventCard).toHaveAttribute('href','/events/event-1');
    expect(screen.queryByText('View event')).not.toBeInTheDocument();
    expect(screen.queryByRole('link',{name:/Buy ticket/})).not.toBeInTheDocument();
    expect(screen.queryByRole('link',{name:/events\/event-1\/seats/})).not.toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(1000);
    expect(screen.getByText('On sale')).toBeInTheDocument();
    expect(screen.queryByRole('link',{name:/Buy ticket/})).not.toBeInTheDocument();
    expect(screen.queryByText('Tickets open in 00.00.01')).not.toBeInTheDocument();
  });
});

describe('EventDetailPage',()=>{
  it('hides seat selection for a sold-out event',async()=>{
    vi.spyOn(api,'event').mockResolvedValue({id:'event-1',name:'Sold Concert',slug:'sold-concert',description:'Description',imageUrl:'https://example.com/event.jpg',venueName:'Venue',address:'Address',startsAt:'2026-07-22T12:00:00Z',endsAt:'2026-07-22T14:00:00Z',salesStartAt:'2026-07-22T10:00:00Z',salesEndAt:'2026-07-22T11:00:00Z',status:'Published',availabilityStatus:'SoldOut',availableSeatCount:0,totalSeatCount:1,seats:[{id:'seat-1',section:'Main',row:'A',number:1,price:100,currency:'USD'}]});
    renderWithQuery(<MemoryRouter initialEntries={['/events/event-1']}><Routes><Route path="/events/:id" element={<EventDetailPage/>}/></Routes></MemoryRouter>,false);
    expect(await screen.findByText('SOLD OUT')).toBeInTheDocument();
    expect(screen.queryByRole('link',{name:'Choose seats for Sold Concert'})).not.toBeInTheDocument();
  });

  it('updates purchase availability at sales boundaries',async()=>{
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-07-22T09:59:59Z'));
    vi.spyOn(api,'event').mockResolvedValue({id:'event-1',name:'Concert',slug:'concert',description:'Description',imageUrl:'https://example.com/event.jpg',venueName:'Venue',address:'Address',startsAt:'2026-07-22T12:00:00Z',endsAt:'2026-07-22T14:00:00Z',salesStartAt:'2026-07-22T10:00:00Z',salesEndAt:'2026-07-22T11:00:00Z',status:'Published',seats:[{id:'seat-1',section:'Main',row:'A',number:1,price:100,currency:'USD'}]});
    renderWithQuery(<MemoryRouter initialEntries={['/events/event-1']}><Routes><Route path="/events/:id" element={<EventDetailPage/>}/></Routes></MemoryRouter>,false);

    await vi.waitFor(()=>expect(screen.getByText('SALES OPENING')).toBeInTheDocument());
    expect(screen.getByRole('heading',{name:'Concert'})).toBeInTheDocument();
    expect(screen.getByText('Description')).toBeInTheDocument();
    expect(screen.getAllByText('Venue')).toHaveLength(2);
    expect(screen.getAllByText('Address')).toHaveLength(2);
    expect(screen.getByText('Event time')).toBeInTheDocument();
    expect(screen.getByText('Ticket sales')).toBeInTheDocument();
    expect(screen.queryByRole('link',{name:'Choose seats for Concert'})).not.toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(1000);
    expect(screen.getByText('NOW BOOKING')).toBeInTheDocument();
    expect(screen.getByRole('link',{name:'Choose seats for Concert'})).toHaveAttribute('href','/events/event-1/seats');

    await vi.advanceTimersByTimeAsync(60*60*1000);
    expect(screen.getByText('SALES ENDED')).toBeInTheDocument();
    expect(screen.getByRole('heading',{name:'Sales ended'})).toBeInTheDocument();
    expect(screen.queryByRole('link',{name:'Choose seats for Concert'})).not.toBeInTheDocument();
  });
});

describe('Role routes',()=>{
  it('keeps a guest deep link as login destination',async()=>{
    renderWithQuery(<MemoryRouter initialEntries={['/bookings?from=email']}><App/></MemoryRouter>,false);
    expect(await screen.findByRole('heading',{name:'Sign in to continue'})).toBeInTheDocument();
  });

  it('shows an admin their My tickets navigation and page',async()=>{
    saveAuth({accessToken:'token',accessTokenExpiresAt:'2026-08-01',refreshToken:'refresh',refreshTokenExpiresAt:'2026-08-01'});
    vi.spyOn(api,'me').mockResolvedValue({id:'1',email:'admin@example.com',fullName:'Admin',role:'Admin'});
    vi.spyOn(api,'bookings').mockResolvedValue([]);
    renderWithQuery(<MemoryRouter initialEntries={['/bookings']}><App/></MemoryRouter>,false);
    expect(await screen.findByRole('heading',{name:'My tickets'})).toBeInTheDocument();
    expect(screen.getByTitle('Admin (admin@example.com)')).toHaveAttribute('href','/bookings');
    expect(screen.getByRole('link',{name:'Admin'})).toBeInTheDocument();
  });

  it('shows Resume payment for an active pending checkout', async () => {
    saveAuth({accessToken:'token',accessTokenExpiresAt:'2026-08-01',refreshToken:'refresh',refreshTokenExpiresAt:'2026-08-01'});
    setActiveCheckout({holdId:'hold-12345678',eventId:'event-1'});
    sessionStorage.setItem('flashseat:booking-id:hold-12345678','booking-1');
    sessionStorage.setItem('flashseat:payment-id:hold-12345678','payment-1');
    vi.spyOn(api,'me').mockResolvedValue({id:'1',email:'customer@example.com',fullName:'Customer',role:'Customer'});
    vi.spyOn(api,'hold').mockResolvedValue({id:'hold-12345678',eventId:'event-1',status:'Converted',expiresAt:new Date(Date.now()+300000).toISOString(),items:[],totalAmount:100,currency:'USD'});
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:[]});
    vi.spyOn(api,'payment').mockResolvedValue({id:'payment-1',bookingId:'booking-1',amount:100,currency:'USD',status:'Pending',createdAt:new Date().toISOString()});
    renderWithQuery(<MemoryRouter initialEntries={['/bookings']}><App/></MemoryRouter>,false);
    expect(await screen.findByRole('link',{name:'Resume payment'})).toHaveAttribute('href','/checkout/hold-12345678');
  });

  it('hides Resume payment while already on checkout', async () => {
    saveAuth({accessToken:'token',accessTokenExpiresAt:'2026-08-01',refreshToken:'refresh',refreshTokenExpiresAt:'2026-08-01'});
    setActiveCheckout({holdId:'hold-12345678',eventId:'event-1'});
    sessionStorage.setItem('flashseat:booking-id:hold-12345678','booking-1');
    sessionStorage.setItem('flashseat:payment-id:hold-12345678','payment-1');
    vi.spyOn(api,'me').mockResolvedValue({id:'1',email:'customer@example.com',fullName:'Customer',role:'Customer'});
    vi.spyOn(api,'hold').mockResolvedValue({id:'hold-12345678',eventId:'event-1',status:'Converted',expiresAt:new Date(Date.now()+300000).toISOString(),items:[],totalAmount:100,currency:'USD'});
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:[]});
    vi.spyOn(api,'payment').mockResolvedValue({id:'payment-1',bookingId:'booking-1',amount:100,currency:'USD',status:'Pending',createdAt:new Date().toISOString()});
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><App/></MemoryRouter>,false);
    await waitFor(() => expect(screen.queryByRole('link',{name:'Resume payment'})).not.toBeInTheDocument());
  });

  it('hides and clears Resume payment for a confirmed checkout', async () => {
    saveAuth({accessToken:'token',accessTokenExpiresAt:'2026-08-01',refreshToken:'refresh',refreshTokenExpiresAt:'2026-08-01'});
    setActiveCheckout({holdId:'hold-12345678',eventId:'event-1'});
    sessionStorage.setItem('flashseat:booking-id:hold-12345678','booking-1');
    vi.spyOn(api,'me').mockResolvedValue({id:'1',email:'customer@example.com',fullName:'Customer',role:'Customer'});
    vi.spyOn(api,'hold').mockResolvedValue({id:'hold-12345678',eventId:'event-1',status:'Converted',expiresAt:new Date(Date.now()+300000).toISOString(),items:[],totalAmount:100,currency:'USD'});
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'Confirmed',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:[]});
    renderWithQuery(<MemoryRouter initialEntries={['/bookings']}><App/></MemoryRouter>,false);
    await waitFor(() => {
      expect(screen.queryByRole('link',{name:'Resume payment'})).not.toBeInTheDocument();
      expect(sessionStorage.getItem('flashseat:active-checkout')).toBeNull();
    });
  });
});

describe('AdminEventFormPage',()=>{
  it('renders the stage and seat layout editor',()=>{
    const queryClient=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
    render(<QueryClientProvider client={queryClient}><MemoryRouter initialEntries={['/admin/events/new']}><Routes><Route path="/admin/events/new" element={<AdminEventFormPage/>}/></Routes></MemoryRouter></QueryClientProvider>);
    expect(screen.getByRole('heading',{name:'Create event'})).toBeInTheDocument();
    expect(screen.getByRole('group',{name:'Seat layout'})).toBeInTheDocument();
    expect(screen.getByRole('heading',{name:'Stage shapes'})).toBeInTheDocument();
    expect(screen.getByLabelText('Row seat type')).toBeInTheDocument();
    expect(screen.getByLabelText('Row seat quantity')).toBeInTheDocument();
    expect(screen.getByRole('button',{name:'Add row'})).toBeInTheDocument();
    expect(screen.getByRole('button',{name:'Save draft'})).toBeInTheDocument();
  });

  it('preserves earlier rows when appending another generated row',()=>{
    const type={id:'type-vip',name:'VIP',price:500000,currency:'VND'};
    const first=appendRowWithSeats(emptySeatLayout(),'VIP','A',type,4);
    const firstSeats=first.seats.map(seat=>({ ...seat }));
    const next=appendRowWithSeats(first,'VIP','B',type,2);
    expect(next.rows).toHaveLength(2);
    expect(next.rows[0].seatIds).toEqual(first.rows[0].seatIds);
    expect(next.seats.slice(0,4)).toEqual(firstSeats);
    expect(next.seats.slice(4).map(seat=>seat.number)).toEqual([1,2]);
    expect(next.seats.slice(4).every(seat=>seat.section==='VIP'&&seat.row==='B'&&seat.price===500000&&seat.currency==='VND')).toBe(true);
  });

  it('removes a row and its seats with removeRow', () => {
    const type = { id: 'type-vip', name: 'VIP', price: 500000, currency: 'VND' };
    const withRowA = appendRowWithSeats(emptySeatLayout(), 'VIP', 'A', type, 3);
    const withRowB = appendRowWithSeats(withRowA, 'VIP', 'B', type, 2);
    expect(withRowB.rows).toHaveLength(2);
    expect(withRowB.seats).toHaveLength(5);

    const afterRemoveA = removeRow(withRowB, withRowB.rows[0].id);
    expect(afterRemoveA.rows).toHaveLength(1);
    expect(afterRemoveA.rows[0].label).toBe('B');
    expect(afterRemoveA.seats).toHaveLength(2);
    expect(afterRemoveA.seats.every(s => s.row === 'B')).toBe(true);
  });

  it('allows removing an existing row from the editor UI', () => {
    const onChange = vi.fn();
    const type = { id: 'type-vip', name: 'VIP', price: 500000, currency: 'VND' };
    const layout = appendRowWithSeats(emptySeatLayout(), 'VIP', 'A', type, 4);
    render(<SeatLayoutEditor value={layout} onChange={onChange} />);

    expect(screen.getByText(/Existing rows \(1\)/i)).toBeInTheDocument();
    const removeBtn = screen.getByRole('button', { name: 'Remove row VIP A' });
    fireEvent.click(removeBtn);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      rows: [],
      seats: [],
    }));
  });

  it('allows removing selected row via Delete key in the editor', () => {
    const onChange = vi.fn();
    const type = { id: 'type-vip', name: 'VIP', price: 500000, currency: 'VND' };
    const layout = appendRowWithSeats(emptySeatLayout(), 'VIP', 'A', type, 4);
    render(<SeatLayoutEditor value={layout} onChange={onChange} />);

    // Click row tag or item to select
    fireEvent.click(screen.getByLabelText('Move VIP row A'));
    expect(screen.getByRole('button', { name: 'Delete row VIP A' })).toBeInTheDocument();

    // Press Delete
    fireEvent.keyDown(window, { key: 'Delete' });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      rows: [],
      seats: [],
    }));
  });

  it('selects a stage preset',()=>{
    const onChange=vi.fn();
    render(<SeatLayoutEditor value={emptySeatLayout()} onChange={onChange}/>);
    fireEvent.click(screen.getByRole('button',{name:/Arena/}));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({stageShape:'Arena'}));
  });

  it('starts with empty seat types and allows adding custom seat types',()=>{
    const onChange=vi.fn();
    const layout = emptySeatLayout();
    expect(layout.seatTypes).toEqual([]);
    const { rerender } = render(<SeatLayoutEditor value={layout} onChange={onChange}/>);
    expect(screen.getByText(/No seat types yet/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add row' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Seat type name'), { target: { value: 'VIP Gold' } });
    fireEvent.change(screen.getByLabelText('Seat type price'), { target: { value: '1500000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add type' }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      seatTypes: [expect.objectContaining({ name: 'VIP Gold', price: 1500000, currency: 'VND' })],
    }));

    const withType = { ...layout, seatTypes: [{ id: 'type-vip', name: 'VIP Gold', price: 1500000, currency: 'VND' }] };
    rerender(<SeatLayoutEditor value={withType} onChange={onChange}/>);
    expect(screen.getByRole('button', { name: 'Add row' })).not.toBeDisabled();
    expect(screen.getByRole('option', { name: 'VIP Gold' })).toBeInTheDocument();
  });
});

describe('Seat layout helpers',()=>{
  const seats=[
    {id:'seat-2',section:'Main',row:'A',number:2,price:100,currency:'USD'},
    {id:'seat-1',section:'Main',row:'A',number:1,price:100,currency:'USD'},
  ];
  it('hydrates legacy seats into rows and serializes coordinates',()=>{
    const layout=layoutFromSeats('Thrust',seats);
    expect(layout.stageShape).toBe('Thrust');
    expect(layout.rows[0].seatIds).toEqual(['seat-1','seat-2']);
    expect(serializeSeatLayout(layout)).toHaveLength(2);
    expect(layout.seats.every(seat => typeof seat.layoutX === 'number' && typeof seat.layoutY === 'number')).toBe(true);
  });

  it('hydrates and moves the persisted stage position',()=>{
    const layout = layoutFromSeats('Arena', seats, 22.5, 64.25);
    expect(stagePosition(layout)).toEqual({ x: 22.5, y: 64.25 });
    const moved = translateStage(layout, { x: 100, y: -50 }, { width: 1000, height: 500 }, { width: 100, height: 100 });
    expect(moved.stageX).toBe(32.5);
    expect(moved.stageY).toBe(54.25);
    expect(moved.stageShape).toBe('Arena');
    expect(moved.rows).toEqual(layout.rows);
    expect(moved.seats).toEqual(layout.seats);
  });

  it('uses the legacy stage position and clamps placement to the canvas',()=>{
    const layout = emptySeatLayout();
    expect(stagePosition({ ...layout, stageX: undefined, stageY: undefined })).toEqual(DEFAULT_STAGE_POSITION);
    const placed = placeStage(layout, { x: 0, y: 0 }, { left: 100, top: 50, width: 1000, height: 500 }, { width: 200, height: 100 });
    expect(placed.stageX).toBe(10);
    expect(placed.stageY).toBe(10);
  });

  it('keeps row coordinates when numbering changes',()=>{
    const layout=layoutFromSeats('Thrust', [
      { id:'seat-1', section:'Main', row:'A', number:1, price:100, currency:'USD', layoutX:35, layoutY:40 },
      { id:'seat-2', section:'Main', row:'A', number:2, price:100, currency:'USD', layoutX:45, layoutY:40 },
    ]);
    const before = layout.seats.map(seat => [seat.layoutX, seat.layoutY]);
    const renumbered = renumberLayout(layout);
    expect(renumbered.seats.map(seat => [seat.layoutX, seat.layoutY])).toEqual(before);
  });

  it('moves and rotates only the selected row',()=>{
    const layout = layoutFromSeats('Thrust', [
      { id:'seat-1', section:'Main', row:'A', number:1, price:100, currency:'USD', layoutX:30, layoutY:30 },
      { id:'seat-2', section:'Main', row:'A', number:2, price:100, currency:'USD', layoutX:40, layoutY:30 },
      { id:'seat-3', section:'Main', row:'B', number:1, price:100, currency:'USD', layoutX:30, layoutY:60 },
      { id:'seat-4', section:'Main', row:'B', number:2, price:100, currency:'USD', layoutX:40, layoutY:60 },
    ]);
    const moved = translateRow(layout, layout.rows[0].id, { x: 10, y: 5 });
    expect(moved.seats.find(seat => seat.id === 'seat-1')).toMatchObject({ layoutX: 40, layoutY: 35 });
    expect(moved.seats.find(seat => seat.id === 'seat-3')).toMatchObject({ layoutX: 30, layoutY: 60 });
    const rotated = rotateRow(moved, moved.rows[0].id, 90);
    expect(rotated.seats.find(seat => seat.id === 'seat-1')).toMatchObject({ layoutX: 45, layoutY: 30 });
    expect(rotated.seats.find(seat => seat.id === 'seat-2')).toMatchObject({ layoutX: 45, layoutY: 40 });
    expect(rotated.seats.find(seat => seat.id === 'seat-3')).toMatchObject({ layoutX: 30, layoutY: 60 });
  });
});

describe('AdminEventsPage',()=>{
  const adminEvent=(status:string)=>({id:`event-${status.toLowerCase()}`,name:`${status} event`,slug:`${status.toLowerCase()}-event`,imageUrl:'https://example.com/event.jpg',venueName:'Venue',startsAt:'2027-09-01T12:00:00Z',endsAt:'2027-09-01T14:00:00Z',salesStartAt:'2027-08-01T12:00:00Z',salesEndAt:'2027-09-01T11:00:00Z',minPrice:100,currency:'USD',status});
  const renderAdmin=(status:string)=>{
    vi.spyOn(api,'adminEvents').mockResolvedValue({items:[adminEvent(status)],page:1,pageSize:12,totalCount:1});
    renderWithQuery(<AdminEventsPage/>);
  };

  const openActions = async (status: string) => {
    expect(await screen.findByText(`${status} event`)).toBeInTheDocument();
    const trigger = screen.getByRole('button', { name: `Actions for ${status} event` });
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
  };

  it.each([
    ['Draft',['Edit event','Publish','Cancel','Archive'],['View public event','Return to draft','Republish','Restore draft','Delete']],
    ['Published',['View public event','Return to draft','Cancel'],['Edit event','Publish','Republish','Restore draft','Archive','Delete']],
    ['Cancelled',['Republish','Restore draft','Delete'],['Edit event','Publish','View public event','Return to draft','Cancel','Archive']],
    ['Ended',['Delete'],['Edit event','Publish','Cancel','View public event','Return to draft','Republish','Restore draft','Archive']],
  ] as const)('shows the valid action matrix for %s',async(status,visible,hidden)=>{
    renderAdmin(status);
    await openActions(status);
    visible.forEach(label=>expect(screen.getByRole('menuitem',{name:label})).toBeInTheDocument());
    hidden.forEach(label=>expect(screen.queryByRole('menuitem',{name:label})).not.toBeInTheDocument());
  });

  it('closes the action menu with Escape and an outside click',async()=>{
    renderAdmin('Draft');
    await openActions('Draft');
    const trigger = screen.getByRole('button', { name: 'Actions for Draft event' });
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(trigger);
    expect(screen.getByRole('menuitem',{name:'Publish'})).toBeInTheDocument();
    fireEvent.mouseDown(document.body);
    expect(screen.queryByRole('menuitem',{name:'Publish'})).not.toBeInTheDocument();
  });

  it.each([
    ['Published','Return to draft','unpublishEvent'],
    ['Cancelled','Republish','republishEvent'],
    ['Cancelled','Restore draft','restoreDraftEvent'],
    ['Cancelled','Delete','archiveEvent'],
    ['Ended','Delete','archiveEvent'],
    ['Draft','Archive','archiveEvent'],
  ] as const)('calls %s lifecycle action after confirmation',async(status,label,method)=>{
    const mutation=vi.spyOn(api,method).mockResolvedValue(undefined);
    vi.spyOn(window,'confirm').mockReturnValue(true);
    renderAdmin(status);
    await openActions(status);
    fireEvent.click(screen.getByRole('menuitem',{name:label}));
    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining(status === 'Published' ? 'Return' : label.split(' ')[0]));
    await waitFor(()=>expect(mutation).toHaveBeenCalledWith(`event-${status.toLowerCase()}`));
  });

  it('shows the structured lifecycle error code',async()=>{
    vi.spyOn(window,'confirm').mockReturnValue(true);
    vi.spyOn(api,'archiveEvent').mockRejectedValue(new ApiError(409,{title:'This event has booking activity and cannot be changed.',code:'sales_activity_exists',unavailableSeatIds:[]}));
    renderAdmin('Draft');
    await openActions('Draft');
    fireEvent.click(screen.getByRole('menuitem',{name:'Archive'}));
    expect(await screen.findByRole('alert')).toHaveTextContent('sales_activity_exists: This event has booking activity and cannot be changed.');
  });
});

describe('SeatPage',()=>{
  it('removes only overlapping seats and keeps the remaining bill',async()=>{
    const detail={id:'event-1',name:'Concert',slug:'concert',description:'',imageUrl:'',venueName:'Venue',address:'Address',startsAt:'2026-08-01T12:00:00Z',endsAt:'2026-08-01T14:00:00Z',salesStartAt:'2026-07-01T12:00:00Z',salesEndAt:'2026-08-01T11:00:00Z',status:'Published',seats:[
      {id:'seat-4',section:'Main',row:'A',number:4,price:40,currency:'USD'},
      {id:'seat-5',section:'Main',row:'A',number:5,price:50,currency:'USD'},
      {id:'seat-6',section:'Main',row:'A',number:6,price:60,currency:'USD'},
      {id:'seat-7',section:'Main',row:'A',number:7,price:70,currency:'USD'},
    ]};
    vi.spyOn(api,'event').mockResolvedValue(detail);
    const availability=vi.spyOn(api,'availability')
      .mockResolvedValueOnce(detail.seats.map(seat=>({seatId:seat.id,status:'Available'})))
      .mockResolvedValue(detail.seats.map(seat=>({seatId:seat.id,status:seat.number<=6?'Held':'Available'})));
    const createHold=vi.spyOn(api,'createHold').mockRejectedValue(new ApiError(409,{title:'Seats unavailable',unavailableSeatIds:['seat-5','seat-6']}));

    renderWithQuery(<MemoryRouter initialEntries={['/events/event-1/seats']}><Routes>
      <Route path="/events/:id/seats" element={<SeatPage/>}/>
      <Route path="/checkout/:holdId" element={<div>Checkout reached</div>}/>
    </Routes></MemoryRouter>,false);

    await screen.findByRole('button',{name:/Seat A4/});
    fireEvent.click(screen.getByRole('button',{name:/Seat A5/}));
    fireEvent.click(screen.getByRole('button',{name:/Seat A6/}));
    fireEvent.click(screen.getByRole('button',{name:/Seat A7/}));
    fireEvent.click(screen.getByRole('button',{name:'Pay'}));

    const alert=await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Main A5, Main A6 are no longer available');
    expect(alert).not.toHaveTextContent('A4');
    expect(alert).not.toHaveTextContent('A7');
    await waitFor(()=>expect(availability).toHaveBeenCalledTimes(2));
    expect(createHold).toHaveBeenCalledWith('event-1',['seat-5','seat-6','seat-7']);
    expect(screen.getByRole('button',{name:/Seat A5/})).toBeDisabled();
    expect(screen.getByRole('button',{name:/Seat A6/})).toBeDisabled();
    expect(screen.getByRole('button',{name:/Seat A7/})).toHaveAttribute('aria-pressed','true');
    expect(screen.getByText('Main · A7')).toBeInTheDocument();
    expect(screen.getAllByText('$70.00')).toHaveLength(2);
    expect(screen.getByRole('button',{name:'Pay'})).toBeEnabled();
    expect(screen.queryByText('Checkout reached')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Bank transfer payment QR code')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button',{name:/Seat A7/}));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});

describe('CheckoutPage',()=>{
  const hold={id:'hold-12345678',eventId:'event-1',status:'Active',expiresAt:new Date(Date.now()+300000).toISOString(),items:[{seatId:'seat-1',section:'Main',row:'A',number:1,price:100}],totalAmount:100,currency:'USD'};

  it('automatically creates a booking and renders the PayOS QR inline',async()=>{
    vi.spyOn(api,'hold').mockResolvedValue(hold);
    const createBooking=vi.spyOn(api,'createBooking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:hold.items});
    const createPayment=vi.spyOn(api,'createPayment').mockResolvedValue({id:'payment-1',bookingId:'booking-1',amount:100,currency:'USD',status:'Pending',createdAt:new Date().toISOString(),qrCode:'00020101021238570010A000000727012700069704220113VQRQ0000000000000000000000000000000000000000000000000000000000000000',orderCode:123456,bankId:'MB',accountNumber:'0384064124',accountName:'LE THIEN KHOA',transferDescription:'FS 123456'});
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/></Routes></MemoryRouter>,false);
    await waitFor(()=>expect(createPayment).toHaveBeenCalledWith('booking-1',expect.any(String)));
    expect(createBooking).toHaveBeenCalledWith('hold-12345678');
    expect(await screen.findByLabelText('Bank transfer payment QR code')).toBeInTheDocument();
    expect(screen.getByText('Ngân hàng')).toBeInTheDocument();
    expect(screen.getByText('MB')).toBeInTheDocument();
    expect(screen.getByText('Số tài khoản')).toBeInTheDocument();
    expect(screen.getByText('LE THIEN KHOA')).toBeInTheDocument();
    expect(screen.getByText('FS 123456')).toBeInTheDocument();
    expect(screen.queryByRole('link',{name:'Mở trang thanh toán PayOS'})).not.toBeInTheDocument();
    expect(screen.queryByRole('button',{name:'Thanh toán qua PayOS'})).not.toBeInTheDocument();
    expect(sessionStorage.getItem('flashseat:payment-key:hold-12345678')).toBeTruthy();
    expect(JSON.parse(sessionStorage.getItem('flashseat:active-checkout') ?? '{}')).toEqual({holdId:'hold-12345678',eventId:'event-1'});
  });

  it('resumes an existing booking and payment without opening PayOS',async()=>{
    sessionStorage.setItem('flashseat:booking-id:hold-12345678','booking-1');
    sessionStorage.setItem('flashseat:payment-id:hold-12345678','payment-1');
    vi.spyOn(api,'hold').mockResolvedValue(hold);
    const createBooking=vi.spyOn(api,'createBooking');
    const createPayment=vi.spyOn(api,'createPayment');
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:hold.items});
    vi.spyOn(api,'payment').mockResolvedValue({id:'payment-1',bookingId:'booking-1',amount:100,currency:'USD',status:'Pending',createdAt:new Date().toISOString(),qrCode:'payos-qr'});
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/></Routes></MemoryRouter>,false);
    expect(await screen.findByLabelText('Bank transfer payment QR code')).toBeInTheDocument();
    expect(createBooking).not.toHaveBeenCalled();
    expect(createPayment).not.toHaveBeenCalled();
    expect(screen.queryByRole('link',{name:/PayOS/i})).not.toBeInTheDocument();
  });

  it('releases a pending payment hold before returning to the seat map',async()=>{
    sessionStorage.setItem('flashseat:booking-id:hold-12345678','booking-1');
    sessionStorage.setItem('flashseat:payment-id:hold-12345678','payment-1');
    sessionStorage.setItem('flashseat:payment-key:hold-12345678','payment-key');
    vi.spyOn(api,'hold').mockResolvedValue(hold);
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:hold.items});
    vi.spyOn(api,'payment').mockResolvedValue({id:'payment-1',bookingId:'booking-1',amount:100,currency:'USD',status:'Pending',createdAt:new Date().toISOString(),qrCode:'payos-qr'});
    const releaseHold=vi.spyOn(api,'releaseHold').mockResolvedValue();
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/><Route path="/events/:id/seats" element={<div>Seat map reached</div>}/></Routes></MemoryRouter>,false);

    expect(await screen.findByRole('button',{name:'Back to seat selection'})).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button',{name:'Back to seat selection'}));

    await waitFor(()=>expect(releaseHold).toHaveBeenCalledWith('hold-12345678'));
    expect(await screen.findByText('Seat map reached')).toBeInTheDocument();
    expect(sessionStorage.getItem('flashseat:booking-id:hold-12345678')).toBeNull();
    expect(sessionStorage.getItem('flashseat:payment-id:hold-12345678')).toBeNull();
    expect(sessionStorage.getItem('flashseat:payment-key:hold-12345678')).toBeNull();
    expect(sessionStorage.getItem('flashseat:active-checkout')).toBeNull();
  });

  it('stays on checkout and preserves session state when release fails',async()=>{
    sessionStorage.setItem('flashseat:booking-id:hold-12345678','booking-1');
    sessionStorage.setItem('flashseat:payment-id:hold-12345678','payment-1');
    sessionStorage.setItem('flashseat:payment-key:hold-12345678','payment-key');
    vi.spyOn(api,'hold').mockResolvedValue(hold);
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:hold.items});
    vi.spyOn(api,'payment').mockResolvedValue({id:'payment-1',bookingId:'booking-1',amount:100,currency:'USD',status:'Pending',createdAt:new Date().toISOString(),qrCode:'payos-qr'});
    vi.spyOn(api,'releaseHold').mockRejectedValue(new ApiError(409,{title:'This checkout cannot release its seats.',code:'hold_not_releasable',unavailableSeatIds:[]}));
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/><Route path="/events/:id/seats" element={<div>Seat map reached</div>}/></Routes></MemoryRouter>,false);

    fireEvent.click(await screen.findByRole('button',{name:'Back to seat selection'}));

    expect(await screen.findByRole('alert')).toHaveTextContent('This checkout cannot release its seats.');
    expect(screen.queryByText('Seat map reached')).not.toBeInTheDocument();
    expect(sessionStorage.getItem('flashseat:booking-id:hold-12345678')).toBe('booking-1');
    expect(sessionStorage.getItem('flashseat:payment-id:hold-12345678')).toBe('payment-1');
    expect(sessionStorage.getItem('flashseat:payment-key:hold-12345678')).toBe('payment-key');
    expect(sessionStorage.getItem('flashseat:active-checkout')).toEqual(JSON.stringify({holdId:'hold-12345678',eventId:'event-1'}));
  });

  it('shows completion actions when payment is confirmed',async()=>{
    sessionStorage.setItem('flashseat:booking-id:hold-12345678','booking-1');
    vi.spyOn(api,'hold').mockResolvedValue(hold);
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'Confirmed',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),confirmedAt:new Date().toISOString(),items:hold.items});
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/></Routes></MemoryRouter>,false);
    expect(await screen.findByRole('heading',{name:'Your booking is confirmed.'})).toBeInTheDocument();
    expect(screen.getByText('FS-1')).toBeInTheDocument();
    expect(screen.getByRole('link',{name:'View ticket detail'})).toHaveAttribute('href','/bookings/booking-1');
    expect(screen.getByRole('link',{name:'Back to event'})).toHaveAttribute('href','/events/event-1');
    expect(screen.queryByLabelText(/seconds remaining/)).not.toBeInTheDocument();
  });

  it('renders expired state and releases hold when time runs out',async()=>{
    const expiredHold={...hold,expiresAt:new Date(Date.now()-10000).toISOString(),status:'Expired'};
    vi.spyOn(api,'hold').mockResolvedValue(expiredHold);
    const releaseHold=vi.spyOn(api,'releaseHold').mockResolvedValue();
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/></Routes></MemoryRouter>,false);
    expect(await screen.findByText('Hết thời gian giữ chỗ')).toBeInTheDocument();
    expect(screen.queryByLabelText('Bank transfer payment QR code')).not.toBeInTheDocument();
    expect(screen.getByRole('link',{name:'Back to seat selection'})).toBeInTheDocument();
    expect(releaseHold).toHaveBeenCalledWith('hold-12345678');
  });
});

describe('MyBookingsPage',()=>{
  it('renders booking items using booking seat IDs',async()=>{
    vi.spyOn(api,'bookings').mockResolvedValue([{id:'booking-1',bookingNumber:'FS-001',eventId:'event-1',status:'Confirmed',totalAmount:200,currency:'USD',createdAt:'2026-07-17T12:00:00Z',items:[{seatId:'seat-1',section:'Main',row:'A',number:1,price:100},{seatId:'seat-2',section:'Main',row:'A',number:2,price:100}]}]);
    renderWithQuery(<MyBookingsPage/>);
    expect(await screen.findByText('Main A1')).toBeInTheDocument();
    expect(screen.getByText('Main A2')).toBeInTheDocument();
  });

  it('shows event information and a view tickets link',async()=>{
    vi.spyOn(api,'bookings').mockResolvedValue([{id:'booking-2',bookingNumber:'FS-002',eventId:'event-2',status:'Confirmed',totalAmount:100,currency:'USD',createdAt:'2026-07-17T12:00:00Z',event:{id:'event-2',name:'Night Market',slug:'night-market',description:'',imageUrl:'',venueName:'Main Hall',address:'1 Main Street',startsAt:'2026-08-20T12:00:00Z',endsAt:'2026-08-20T14:00:00Z',status:'Published'},items:[{id:'item-1',seatId:'seat-1',section:'Main',row:'A',number:1,price:100,currency:'USD',ticketCode:'0123456789ABCDEF0123456789ABCDEF',checkInStatus:'NotCheckedIn'}]}]);
    renderWithQuery(<MyBookingsPage/>);
    expect(await screen.findByText('Night Market')).toBeInTheDocument();
    expect(screen.getByText((content) => content.startsWith('Main Hall ·'))).toBeInTheDocument();
    expect(screen.getByRole('link',{name:'View tickets'})).toHaveAttribute('href','/bookings/booking-2');
  });

  it('hides pending bookings even when they contain ticket-shaped items',async()=>{
    vi.spyOn(api,'bookings').mockResolvedValue([{id:'pending-1',bookingNumber:'FS-PENDING',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:'2026-07-17T12:00:00Z',items:[{id:'item-pending',seatId:'seat-1',section:'Main',row:'A',number:1,price:100,ticketCode:'PREPAYMENT-CODE'}]}]);
    renderWithQuery(<MyBookingsPage/>);
    expect(await screen.findByText("You don't have any confirmed tickets yet.")).toBeInTheDocument();
    expect(screen.queryByText('FS-PENDING')).not.toBeInTheDocument();
    expect(screen.queryByRole('link',{name:'View tickets'})).not.toBeInTheDocument();
  });

  it('renders user profile details when current user is logged in',async()=>{
    vi.spyOn(api,'bookings').mockResolvedValue([]);
    vi.spyOn(api,'me').mockResolvedValue({id:'u-1',email:'tester@example.com',fullName:'Alex Rivera',role:'Customer'});
    renderWithQuery(<MyBookingsPage/>);
    expect(await screen.findByRole('heading',{name:'Alex Rivera',level:2})).toBeInTheDocument();
    expect(screen.getByText('tester@example.com')).toBeInTheDocument();
    expect(screen.getByText('Customer')).toBeInTheDocument();
    expect(screen.getByText('AL')).toBeInTheDocument();
  });
});

describe('BookingDetailPage',()=>{
  it('renders one QR for each confirmed ticket',async()=>{
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-3',bookingNumber:'FS-003',eventId:'event-3',status:'Confirmed',totalAmount:200,currency:'USD',createdAt:'2026-07-17T12:00:00Z',event:{id:'event-3',name:'Arena Show',slug:'arena-show',description:'',imageUrl:'',venueName:'Arena',address:'Address',startsAt:'2026-08-20T12:00:00Z',endsAt:'2026-08-20T14:00:00Z',status:'Published'},items:[{id:'item-1',seatId:'seat-1',section:'Main',row:'A',number:1,price:100,currency:'USD',ticketCode:'0123456789ABCDEF0123456789ABCDEF',checkInStatus:'NotCheckedIn'},{id:'item-2',seatId:'seat-2',section:'Main',row:'A',number:2,price:100,currency:'USD',ticketCode:'FEDCBA9876543210FEDCBA9876543210',checkInStatus:'NotCheckedIn'}]});
    renderWithQuery(<MemoryRouter initialEntries={['/bookings/booking-3']}><Routes><Route path="/bookings/:id" element={<BookingDetailPage/>}/></Routes></MemoryRouter>,false);
    expect(await screen.findByRole('heading',{name:'Arena Show',level:1})).toBeInTheDocument();
    expect(screen.getAllByRole('img')).toHaveLength(2);
    expect(screen.getAllByText(/0123456789|FEDCBA9876/)).toHaveLength(2);
  });

  it('does not render ticket details for a pending booking',async()=>{
    vi.spyOn(api,'booking').mockResolvedValue({id:'pending-2',bookingNumber:'FS-PENDING',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:'2026-07-17T12:00:00Z',event:null,items:[{id:'item-pending',seatId:'seat-1',section:'Main',row:'A',number:1,price:100,ticketCode:'PREPAYMENT-CODE'}]});
    renderWithQuery(<MemoryRouter initialEntries={['/bookings/pending-2']}><Routes><Route path="/bookings/:id" element={<BookingDetailPage/>}/></Routes></MemoryRouter>,false);
    expect(await screen.findByText('This booking is not confirmed, so no ticket has been issued.')).toBeInTheDocument();
    expect(screen.queryByText('PREPAYMENT-CODE')).not.toBeInTheDocument();
    expect(screen.queryByText('QR available after payment')).not.toBeInTheDocument();
  });
});

describe('CheckInPage',()=>{
  const event = { id:'event-1', name:'Arena Show', slug:'arena-show', imageUrl:'', venueName:'Main Hall', startsAt:'2026-08-20T12:00:00Z', endsAt:'2026-08-20T14:00:00Z', salesStartAt:'2026-08-19T12:00:00Z', salesEndAt:'2026-08-20T12:00:00Z', minPrice:100, currency:'USD', status:'Published' };

  it('requires an event before submitting a manually entered ticket code',async()=>{
    vi.spyOn(api,'events').mockResolvedValue({items:[event],page:1,pageSize:100,totalCount:1});
    const checkIn = vi.spyOn(api,'checkIn').mockResolvedValue({ticketCode:'0123456789ABCDEF0123456789ABCDEF',status:'CheckedIn',checkedInAt:'2026-08-20T13:00:00Z',bookingNumber:'FS-004',event:null,ticket:{id:'item-1',seatId:'seat-1',section:'Main',row:'A',number:1,price:100,currency:'USD',ticketCode:'0123456789ABCDEF0123456789ABCDEF',checkInStatus:'CheckedIn'}});
    renderWithQuery(<CheckInPage/>);
    expect(screen.getByRole('button',{name:'Scan with camera'})).toBeDisabled();
    expect(screen.getByRole('button',{name:'Check in ticket'})).toBeDisabled();
    fireEvent.change(await screen.findByLabelText('Event'),{target:{value:'event-1'}});
    fireEvent.change(screen.getByLabelText('Ticket code'),{target:{value:'FS1:0123456789ABCDEF0123456789ABCDEF'}});
    fireEvent.click(screen.getByRole('button',{name:'Check in ticket'}));
    expect(await screen.findByText('Ticket checked in.')).toBeInTheDocument();
    expect(checkIn).toHaveBeenCalledWith('event-1','FS1:0123456789ABCDEF0123456789ABCDEF');
  });

  it('shows an event mismatch without clearing the selected event',async()=>{
    vi.spyOn(api,'events').mockResolvedValue({items:[event],page:1,pageSize:100,totalCount:1});
    vi.spyOn(api,'checkIn').mockRejectedValue(new ApiError(409,{code:'ticket_event_mismatch',title:'This ticket belongs to another event.',unavailableSeatIds:[],body:{code:'ticket_event_mismatch',title:'This ticket belongs to another event.'}}));
    renderWithQuery(<CheckInPage/>);
    fireEvent.change(await screen.findByLabelText('Event'),{target:{value:'event-1'}});
    fireEvent.change(screen.getByLabelText('Ticket code'),{target:{value:'FS1:0123456789ABCDEF0123456789ABCDEF'}});
    fireEvent.click(screen.getByRole('button',{name:'Check in ticket'}));
    expect(await screen.findByText('This ticket belongs to another event.')).toBeInTheDocument();
    expect(screen.getByLabelText('Event')).toHaveValue('event-1');
  });
});
