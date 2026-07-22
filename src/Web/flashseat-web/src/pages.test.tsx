import { type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminEventFormPage } from './admin-pages';
import { ApiError, api, logout, saveAuth } from './api';
import App from './App';
import { AuthPage, CheckoutPage, EventDetailPage, HomePage, MyBookingsPage, SeatPage } from './pages';

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
});

describe('HomePage',()=>{
  it('paginates and resets to page one when searching',async()=>{
    const events=vi.spyOn(api,'events').mockImplementation(async(search,page=1)=>({items:[{id:`event-${page}`,name:search||`Event ${page}`,slug:'event',imageUrl:'https://example.com/event.jpg',venueName:'Venue',startsAt:'2026-08-01T12:00:00Z',minPrice:100,currency:'USD',status:'Published'}],page,pageSize:12,totalCount:24}));
    renderWithQuery(<HomePage/>);
    await screen.findByText('Event 1');
    fireEvent.click(screen.getByRole('button',{name:'Next'}));
    await waitFor(()=>expect(events).toHaveBeenLastCalledWith('',2));
    fireEvent.change(screen.getByLabelText('Search the listings'),{target:{value:'Jazz'}});
    await waitFor(()=>expect(events).toHaveBeenLastCalledWith('Jazz',1));
  });
});

describe('EventDetailPage',()=>{
  it('updates purchase availability at sales boundaries',async()=>{
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-07-22T09:59:59Z'));
    vi.spyOn(api,'event').mockResolvedValue({id:'event-1',name:'Concert',slug:'concert',description:'Description',imageUrl:'https://example.com/event.jpg',venueName:'Venue',address:'Address',startsAt:'2026-07-22T12:00:00Z',salesStartAt:'2026-07-22T10:00:00Z',salesEndAt:'2026-07-22T11:00:00Z',status:'Published',seats:[{id:'seat-1',section:'Main',row:'A',number:1,price:100,currency:'USD'}]});
    renderWithQuery(<MemoryRouter initialEntries={['/events/event-1']}><Routes><Route path="/events/:id" element={<EventDetailPage/>}/></Routes></MemoryRouter>,false);

    await vi.waitFor(()=>expect(screen.getByText('SALES OPENING')).toBeInTheDocument());
    expect(screen.queryByRole('link',{name:'Choose seats'})).not.toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(1000);
    expect(screen.getByText('NOW BOOKING')).toBeInTheDocument();
    expect(screen.getByRole('link',{name:'Choose seats'})).toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(60*60*1000);
    expect(screen.getByText('SALES ENDED')).toBeInTheDocument();
    expect(screen.getByRole('heading',{name:'Sales ended'})).toBeInTheDocument();
    expect(screen.queryByRole('link',{name:'Choose seats'})).not.toBeInTheDocument();
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
    expect(screen.getByRole('link',{name:'My tickets'})).toBeInTheDocument();
    expect(screen.getByRole('link',{name:'Admin'})).toBeInTheDocument();
  });
});

describe('AdminEventFormPage',()=>{
  it('renders an English create form with one seat',()=>{
    const queryClient=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
    render(<QueryClientProvider client={queryClient}><MemoryRouter initialEntries={['/admin/events/new']}><Routes><Route path="/admin/events/new" element={<AdminEventFormPage/>}/></Routes></MemoryRouter></QueryClientProvider>);
    expect(screen.getByRole('heading',{name:'Create event'})).toBeInTheDocument();
    expect(screen.getByRole('group',{name:'Event details'})).toBeInTheDocument();
    expect(screen.getByRole('group',{name:'Venue'})).toBeInTheDocument();
    expect(screen.getByRole('group',{name:'Schedule'})).toBeInTheDocument();
    expect(screen.getByRole('group',{name:'Seat inventory'})).toBeInTheDocument();
    expect(screen.getAllByLabelText('Section')).toHaveLength(1);
    expect(screen.getByRole('button',{name:'Remove'})).toBeDisabled();
    fireEvent.click(screen.getByRole('button',{name:'Add seat'}));
    expect(screen.getAllByLabelText('Section')).toHaveLength(2);
    expect(screen.getByRole('button',{name:'Save draft'})).toBeInTheDocument();
  });

  it('renumbers seats after removing one',()=>{
    const queryClient=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
    render(<QueryClientProvider client={queryClient}><MemoryRouter initialEntries={['/admin/events/new']}><Routes><Route path="/admin/events/new" element={<AdminEventFormPage/>}/></Routes></MemoryRouter></QueryClientProvider>);
    for(let index=1;index<6;index++) fireEvent.click(screen.getByRole('button',{name:'Add seat'}));
    fireEvent.click(screen.getAllByRole('button',{name:'Remove'})[4]);
    expect(screen.getAllByLabelText('Number')).toHaveLength(5);
    expect(screen.getAllByLabelText('Number').map(input=>(input as HTMLInputElement).value)).toEqual(['1','2','3','4','5']);
    fireEvent.click(screen.getByRole('button',{name:'Add seat'}));
    expect(screen.getAllByLabelText('Number').map(input=>(input as HTMLInputElement).value)).toEqual(['1','2','3','4','5','6']);
  });

  it('numbers seats independently for each row',()=>{
    const queryClient=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
    render(<QueryClientProvider client={queryClient}><MemoryRouter initialEntries={['/admin/events/new']}><Routes><Route path="/admin/events/new" element={<AdminEventFormPage/>}/></Routes></MemoryRouter></QueryClientProvider>);
    fireEvent.click(screen.getByRole('button',{name:'Add seat'}));
    fireEvent.change(screen.getAllByLabelText('Row')[1],{target:{value:'B'}});
    fireEvent.change(screen.getAllByLabelText('Number')[1],{target:{value:'1'}});
    fireEvent.click(screen.getByRole('button',{name:'Add seat'}));
    expect(screen.getAllByLabelText('Row').map(input=>(input as HTMLInputElement).value)).toEqual(['A','B','B']);
    expect(screen.getAllByLabelText('Number').map(input=>(input as HTMLInputElement).value)).toEqual(['1','1','2']);
    fireEvent.click(screen.getAllByRole('button',{name:'Remove'})[1]);
    expect(screen.getAllByLabelText('Row').map(input=>(input as HTMLInputElement).value)).toEqual(['A','B']);
    expect(screen.getAllByLabelText('Number').map(input=>(input as HTMLInputElement).value)).toEqual(['1','1']);
  });
});

describe('SeatPage',()=>{
  it('removes only overlapping seats and keeps the remaining bill',async()=>{
    const detail={id:'event-1',name:'Concert',slug:'concert',description:'',imageUrl:'',venueName:'Venue',address:'Address',startsAt:'2026-08-01T12:00:00Z',salesStartAt:'2026-07-01T12:00:00Z',salesEndAt:'2026-08-01T11:00:00Z',status:'Published',seats:[
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
    expect(screen.queryByAltText('FlashSeat demo payment QR; no real payment')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button',{name:/Seat A7/}));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});

describe('CheckoutPage',()=>{
  const hold={id:'hold-12345678',eventId:'event-1',status:'Active',expiresAt:new Date(Date.now()+300000).toISOString(),items:[{seatId:'seat-1',section:'Main',row:'A',number:1,price:100}],totalAmount:100,currency:'USD'};

  it('shows held seats and does not pay on mount',async()=>{
    vi.spyOn(api,'hold').mockResolvedValue(hold);
    const createBooking=vi.spyOn(api,'createBooking');
    const createPayment=vi.spyOn(api,'createPayment');
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/></Routes></MemoryRouter>,false);
    expect(await screen.findByText('Held')).toBeInTheDocument();
    expect(screen.getByAltText('FlashSeat demo payment QR; no real payment')).toBeInTheDocument();
    expect(screen.getByText(/no real money is transferred/)).toBeInTheDocument();
    expect(createBooking).not.toHaveBeenCalled();
    expect(createPayment).not.toHaveBeenCalled();
  });

  it('creates booking and payment only after confirmation',async()=>{
    vi.spyOn(api,'hold').mockResolvedValue(hold);
    vi.spyOn(api,'createBooking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'PendingPayment',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:hold.items});
    const createPayment=vi.spyOn(api,'createPayment').mockResolvedValue({id:'payment-1',bookingId:'booking-1',amount:100,currency:'USD',status:'Succeeded',createdAt:new Date().toISOString()});
    vi.spyOn(api,'booking').mockResolvedValue({id:'booking-1',bookingNumber:'FS-1',eventId:'event-1',status:'Confirmed',totalAmount:100,currency:'USD',createdAt:new Date().toISOString(),items:hold.items});
    renderWithQuery(<MemoryRouter initialEntries={['/checkout/hold-12345678']}><Routes><Route path="/checkout/:holdId" element={<CheckoutPage/>}/></Routes></MemoryRouter>,false);
    fireEvent.click(await screen.findByRole('button',{name:'Confirm demo payment'}));
    await screen.findByText('Payment confirmed.');
    expect(createPayment).toHaveBeenCalledWith('booking-1','Success',expect.any(String));
    expect(sessionStorage.getItem('flashseat:payment-key:hold-12345678')).toBeTruthy();
  });
});

describe('MyBookingsPage',()=>{
  it('renders booking items using booking seat IDs',async()=>{
    vi.spyOn(api,'bookings').mockResolvedValue([{id:'booking-1',bookingNumber:'FS-001',eventId:'event-1',status:'Confirmed',totalAmount:200,currency:'USD',createdAt:'2026-07-17T12:00:00Z',items:[{seatId:'seat-1',section:'Main',row:'A',number:1,price:100},{seatId:'seat-2',section:'Main',row:'A',number:2,price:100}]}]);
    renderWithQuery(<MyBookingsPage/>);
    expect(await screen.findByText('Main A1')).toBeInTheDocument();
    expect(screen.getByText('Main A2')).toBeInTheDocument();
  });
});
