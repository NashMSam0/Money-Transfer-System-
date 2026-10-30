import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { finalize, tap } from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly sessionKey = 'shameen.tabSession';
  authStatus() { return this.http.get<{ needsAdministratorSetup: boolean }>('/api/auth/status'); }
  me() { return this.http.get<any>('/api/auth/me'); }
  login(credentials: { email: string; password: string }) { return this.http.post<any>('/api/auth/login', credentials).pipe(tap(response => this.storeSession(response))); }
  changeOwnPassword(currentPassword: string, newPassword: string) { return this.http.put('/api/auth/password', { currentPassword, newPassword }); }
  setupAdministrator(account: { name: string; email: string; password: string }) { return this.http.post<any>('/api/auth/setup', account).pipe(tap(response => this.storeSession(response))); }
  logout() { return this.http.post('/api/auth/logout', {}).pipe(finalize(() => sessionStorage.removeItem(this.sessionKey))); }
  sessionToken() { return sessionStorage.getItem(this.sessionKey); }
  private storeSession(response: any) { if (response?.sessionToken) sessionStorage.setItem(this.sessionKey, response.sessionToken); }
  getBranches() { return this.http.get<any[]>('/api/branches'); }
  getLocations() { return this.http.get<Array<{id:number;name:string;city:string;country:string}>>('/api/locations'); }
  getDestinationCities() { return this.http.get<Array<{country:string;city:string}>>('/api/destination-cities'); }
  loadWorkspace(date = new Date().toISOString().slice(0, 10), branchId = 1) {
    const query = `?date=${encodeURIComponent(date)}&branchId=${branchId}`;
    return forkJoin({
      dashboard: this.http.get<any>('/api/dashboard' + query),
      employees: this.http.get<any[]>('/api/employees?branchId=' + branchId),
      transfers: this.http.get<any[]>('/api/transfers' + query),
      day: this.http.get<any>('/api/day/current?branchId=' + branchId)
    });
  }
  openDay(openingFloat: number, branchId: number) { return this.http.post('/api/day/open', { openingFloat, employeeId: null, branchId }); }
  createTransfer(transfer: { employeeId: number; branchId: number; customerName: string; destinationCountry: string; destinationCity: string; amount: number; payoutMethod: string }) { return this.http.post<any>('/api/transfers', transfer); }
  confirmCollection(id: number, collectionReference: string) { return this.http.post<any>('/api/transfers/' + id + '/collection', { collectionReference }); }
  updateTransferDestinationCity(id: number, destinationCity: string) { return this.http.put<any>('/api/transfers/' + id + '/destination-city', { destinationCity }); }
  getCollectionQueue(date = new Date().toISOString().slice(0, 10)) { return this.http.get<any>('/api/collections?date=' + encodeURIComponent(date)); }
  setEmployeeCollectionCountry(id: number, country: string | null) { return this.http.put('/api/employees/' + id + '/collection-country', { country }); }
  setEmployeeLocation(id: number, branchId: number) { return this.http.put<any>('/api/employees/' + id + '/location', { branchId }); }
  createEmployee(employee: { name: string; email: string; role: string; branchId: number; password: string }) { return this.http.post<any>('/api/employees', employee); }
  setEmployeePassword(id: number, password: string) { return this.http.put('/api/employees/' + id + '/credentials', { password }); }
  enterMyFloat(amount: number) { return this.http.post<any>('/api/float/my', { amount, reason: null }); }
  adjustEmployeeFloat(id: number, amount: number, reason: string) { return this.http.put<any>('/api/employees/' + id + '/float', { amount, reason }); }
  closeDay(branchId: number) { return this.http.post<any>('/api/day/close', { employeeId: null, branchId }); }
}
