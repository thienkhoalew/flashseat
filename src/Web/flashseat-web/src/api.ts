export type EventItem = { id:string; name:string; slug:string; imageUrl:string; venueName:string; startsAt:string; minPrice:number; currency:string; status:string };
export type Seat = { id:string; section:string; row:string; number:number; price:number; currency:string };
export type EventDetail = EventItem & { description:string; address:string; salesStartAt:string; salesEndAt:string; seats:Seat[] };
export type Availability = { seatId:string; status:string; holdExpiresAt?:string };
export type Booking = { id:string; bookingNumber:string; eventId:string; status:string; totalAmount:number; currency:string; createdAt:string; items:Seat[] };
export type AuthResponse = { accessToken:string; accessTokenExpiresAt:string; refreshToken:string; refreshTokenExpiresAt:string };
const base = import.meta.env.VITE_API_URL ?? '';
let accessToken = localStorage.getItem('accessToken');
let refreshToken = sessionStorage.getItem('refreshToken');
export function isAuthenticated(){ return !!accessToken; }
export function logout(){ accessToken=null; refreshToken=null; localStorage.removeItem('accessToken'); sessionStorage.removeItem('refreshToken'); window.dispatchEvent(new Event('auth-changed')); }
export function saveAuth(auth:AuthResponse){ accessToken=auth.accessToken; refreshToken=auth.refreshToken; localStorage.setItem('accessToken',auth.accessToken); sessionStorage.setItem('refreshToken',auth.refreshToken); window.dispatchEvent(new Event('auth-changed')); }
async function request<T>(path:string, init:RequestInit={}, retry=true):Promise<T>{
  const headers = new Headers(init.headers); headers.set('Content-Type','application/json'); if(accessToken) headers.set('Authorization',`Bearer ${accessToken}`);
  const response=await fetch(`${base}${path}`,{...init,headers});
  if(response.status===401&&retry&&refreshToken){ const refreshed=await fetch(`${base}/api/auth/refresh`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({refreshToken})}); if(refreshed.ok){ saveAuth(await refreshed.json()); return request<T>(path,init,false); } logout(); }
  if(!response.ok){ const problem=await response.json().catch(()=>({title:'Đã có lỗi xảy ra'})); throw Object.assign(new Error(problem.title??'Đã có lỗi xảy ra'),{status:response.status,problem}); }
  if(response.status===204) return undefined as T; return response.json();
}
export const api={
  events:(search='')=>request<{items:EventItem[]}>(`/api/events?search=${encodeURIComponent(search)}`), event:(id:string)=>request<EventDetail>(`/api/events/${id}`),
  availability:(id:string)=>request<Availability[]>(`/api/events/${id}/availability`),
  login:(email:string,password:string)=>request<AuthResponse>('/api/auth/login',{method:'POST',body:JSON.stringify({email,password})}),
  register:(email:string,password:string,fullName:string)=>request<AuthResponse>('/api/auth/register',{method:'POST',body:JSON.stringify({email,password,fullName})}),
  hold:(eventId:string,seatIds:string[])=>request<{id:string;expiresAt:string;totalAmount:number}>('/api/seat-holds',{method:'POST',body:JSON.stringify({eventId,seatIds})}),
  booking:(holdId:string)=>request<Booking>('/api/bookings',{method:'POST',body:JSON.stringify({holdId})}),
  pay:(bookingId:string,result:string)=>request<{id:string;status:string}>(`/api/payments`,{method:'POST',headers:{'Idempotency-Key':crypto.randomUUID()},body:JSON.stringify({bookingId,simulateResult:result})}),
  bookings:()=>request<Booking[]>('/api/bookings/me'),
};
export const money=(value:number)=>new Intl.NumberFormat('vi-VN',{style:'currency',currency:'VND'}).format(value);
export const date=(value:string)=>new Intl.DateTimeFormat('vi-VN',{dateStyle:'medium',timeStyle:'short'}).format(new Date(value));
