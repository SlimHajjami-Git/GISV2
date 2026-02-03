import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export interface Toast {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  title: string;
  message: string;
  duration: number;
}

@Injectable({
  providedIn: 'root'
})
export class ToastService {
  private toasts$ = new BehaviorSubject<Toast[]>([]);

  getToasts() {
    return this.toasts$.asObservable();
  }

  success(title: string, message: string, duration: number = 5000) {
    this.show('success', title, message, duration);
  }

  error(title: string, message: string, duration: number = 5000) {
    this.show('error', title, message, duration);
  }

  warning(title: string, message: string, duration: number = 5000) {
    this.show('warning', title, message, duration);
  }

  info(title: string, message: string, duration: number = 5000) {
    this.show('info', title, message, duration);
  }

  private show(type: Toast['type'], title: string, message: string, duration: number) {
    const id = this.generateId();
    const toast: Toast = { id, type, title, message, duration };
    
    const current = this.toasts$.value;
    this.toasts$.next([...current, toast]);

    if (duration > 0) {
      setTimeout(() => this.dismiss(id), duration);
    }
  }

  dismiss(id: string) {
    const current = this.toasts$.value;
    this.toasts$.next(current.filter(t => t.id !== id));
  }

  dismissAll() {
    this.toasts$.next([]);
  }

  private generateId(): string {
    return Math.random().toString(36).substring(2, 11);
  }
}
