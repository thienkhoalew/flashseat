export type EventItem = { id:string; name:string; slug:string; imageUrl:string; venueName:string; startsAt:string; minPrice:number; currency:string; status:string };
export type Seat = { id:string; section:string; row:string; number:number; price:number; currency:string };
export type SeatInput = Omit<Seat,'id'>;
export type EventDetail = { id:string; name:string; slug:string; description:string; imageUrl:string; venueName:string; address:string; startsAt:string; salesStartAt:string; salesEndAt:string; status:string; seats:Seat[] };
export type SaveEventInput = Pick<EventDetail,'name'|'slug'|'description'|'imageUrl'|'venueName'|'address'|'startsAt'|'salesStartAt'|'salesEndAt'> & { seats:SeatInput[] };
export type PagedResponse<T> = { items:T[]; page:number; pageSize:number; totalCount:number };
export type Availability = { seatId:string; status:string; holdExpiresAt?:string };
export type HoldItem = { seatId:string; section:string; row:string; number:number; price:number };
export type Hold = { id:string; eventId:string; status:string; expiresAt:string; items:HoldItem[]; totalAmount:number; currency:string };
export type BookingItem = HoldItem;
export type Booking = { id:string; bookingNumber:string; eventId:string; status:string; totalAmount:number; currency:string; createdAt:string; items:BookingItem[] };
export type Payment = { id:string; bookingId:string; amount:number; currency:string; status:string; failureReason?:string; createdAt:string; completedAt?:string };
export type AuthResponse = { accessToken:string; accessTokenExpiresAt:string; refreshToken:string; refreshTokenExpiresAt:string };
export type CurrentUser = { id:string; email:string; fullName:string; role:'Admin'|'Customer' };
export type ApiProblem = { title:string; unavailableSeatIds:string[] };
export class ApiError extends Error {
  constructor(public status:number,public problem:ApiProblem){ super(problem.title); }
}
const base = import.meta.env.VITE_API_URL ?? '';
let accessToken = localStorage.getItem('accessToken');
let refreshToken = sessionStorage.getItem('refreshToken');
export function isAuthenticated(){ return !!accessToken; }
export function logout(){ accessToken=null; refreshToken=null; localStorage.removeItem('accessToken'); sessionStorage.removeItem('refreshToken'); window.dispatchEvent(new Event('auth-changed')); }
export function saveAuth(auth:AuthResponse){ accessToken=auth.accessToken; refreshToken=auth.refreshToken; localStorage.setItem('accessToken',auth.accessToken); sessionStorage.setItem('refreshToken',auth.refreshToken); window.dispatchEvent(new Event('auth-changed')); }
async function request<T>(path:string, init:RequestInit={}, retry=true):Promise<T>{
  const headers = new Headers(init.headers); headers.set('Content-Type','application/json'); if(accessToken) headers.set('Authorization',`Bearer ${accessToken}`);
  const response=await fetch(`${base}${path}`,{...init,headers});
  if(response.status===401&&retry&&refreshToken){ const refreshed=await fetch(`${base}/api/auth/refresh`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({refreshToken})}); if(refreshed.ok){ saveAuth(await refreshed.json()); return request<T>(path,init,false); } }
  if(response.status===401) logout();
  if(!response.ok){
    const body:unknown=await response.json().catch(()=>null);
    const problem={
      title:typeof body==='object'&&body!==null&&'title' in body&&typeof body.title==='string'?body.title:'Something went wrong',
      unavailableSeatIds:typeof body==='object'&&body!==null&&'unavailableSeatIds' in body&&Array.isArray(body.unavailableSeatIds)?body.unavailableSeatIds.filter((id):id is string=>typeof id==='string'):[],
    };
    throw new ApiError(response.status,problem);
  }
  if(response.status===204) return undefined as T; return response.json();
}
export const api={
  events:(search='',page=1,pageSize=12)=>request<PagedResponse<EventItem>>(`/api/events?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`), event:(id:string)=>request<EventDetail>(`/api/events/${id}`),
  availability:(id:string)=>request<Availability[]>(`/api/events/${id}/availability`),
  login:(email:string,password:string)=>request<AuthResponse>('/api/auth/login',{method:'POST',body:JSON.stringify({email,password})}),
  register:(email:string,password:string,fullName:string)=>request<AuthResponse>('/api/auth/register',{method:'POST',body:JSON.stringify({email,password,fullName})}),
  me:()=>request<CurrentUser>('/api/auth/me'),
  createHold:(eventId:string,seatIds:string[])=>request<Hold>('/api/seat-holds',{method:'POST',body:JSON.stringify({eventId,seatIds})}),
  hold:(id:string)=>request<Hold>(`/api/seat-holds/${id}`),
  releaseHold:(id:string)=>request<void>(`/api/seat-holds/${id}`,{method:'DELETE'}),
  createBooking:(holdId:string)=>request<Booking>('/api/bookings',{method:'POST',body:JSON.stringify({holdId})}),
  booking:(id:string)=>request<Booking>(`/api/bookings/${id}`),
  createPayment:(bookingId:string,result:string,key:string)=>request<Payment>('/api/payments',{method:'POST',headers:{'Idempotency-Key':key},body:JSON.stringify({bookingId,simulateResult:result})}),
  payment:(id:string)=>request<Payment>(`/api/payments/${id}`),
  bookings:()=>request<Booking[]>('/api/bookings/me'),
  adminEvents:(search='',page=1,pageSize=12)=>request<PagedResponse<EventItem>>(`/api/admin/events/?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`),
  adminEvent:(id:string)=>request<EventDetail>(`/api/admin/events/${id}`),
  createEvent:(input:SaveEventInput)=>request<EventDetail>('/api/admin/events/',{method:'POST',body:JSON.stringify(input)}),
  updateEvent:(id:string,input:SaveEventInput)=>request<EventDetail>(`/api/admin/events/${id}`,{method:'PUT',body:JSON.stringify(input)}),
  publishEvent:(id:string)=>request<void>(`/api/admin/events/${id}/publish`,{method:'POST'}),
  cancelEvent:(id:string)=>request<void>(`/api/admin/events/${id}/cancel`,{method:'POST'}),
};
export const money=(value:number,currency:string)=>new Intl.NumberFormat('en-US',{style:'currency',currency}).format(value);
export const date=(value:string)=>new Intl.DateTimeFormat('en-US',{dateStyle:'medium',timeStyle:'short'}).format(new Date(value));
