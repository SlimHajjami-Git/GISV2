import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Subject, Subscription, Observable } from 'rxjs';
import { SignalRService, SignalRNotification } from './signalr.service';

export interface Notification {
  id: number;
  userId: number;
  type: string;
  title: string;
  message: string;
  priority: string;
  channel: string;
  isRead: boolean;
  readAt: string | null;
  referenceType: string | null;
  referenceId: number | null;
  actionUrl: string | null;
  metadata: any;
  createdAt: string;
}

export interface NotificationPage {
  items: Notification[];
  totalCount: number;
  unreadCount: number;
  page: number;
  pageSize: number;
}

@Injectable({
  providedIn: 'root'
})
export class NotificationService implements OnDestroy {
  private baseUrl = '/api/notifications';
  private subscriptions: Subscription[] = [];

  public unreadCount$ = new BehaviorSubject<number>(0);
  public newNotification$ = new Subject<SignalRNotification>();
  public notifications$ = new BehaviorSubject<Notification[]>([]);

  constructor(
    private http: HttpClient,
    private signalR: SignalRService
  ) {
    // Listen for real-time notifications from SignalR
    this.subscriptions.push(
      this.signalR.notification$.subscribe(notification => {
        // Dedup: ignore a notification whose id is already in the list
        const current = this.notifications$.value;
        if (current.some(n => n.id === notification.id)) {
          return;
        }
        this.newNotification$.next(notification);
        // Prepend to local list
        const mapped: Notification = {
          id: notification.id,
          userId: 0,
          type: notification.type,
          title: notification.title,
          message: notification.message,
          priority: notification.priority,
          channel: 'push',
          isRead: false,
          readAt: null,
          referenceType: notification.referenceType || null,
          referenceId: notification.referenceId || null,
          actionUrl: notification.actionUrl || null,
          metadata: notification.metadata,
          createdAt: notification.createdAt
        };
        this.notifications$.next([mapped, ...current]);
        // Increment unread count
        this.unreadCount$.next(this.unreadCount$.value + 1);
      })
    );

    // Listen for unread count changes from SignalR
    this.subscriptions.push(
      this.signalR.unreadCount$.subscribe(count => {
        this.unreadCount$.next(count);
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(s => s.unsubscribe());
  }

  /** Load initial unread count (call after login) */
  loadUnreadCount(): void {
    this.http.get<{ count: number }>(`${this.baseUrl}/unread-count`).subscribe({
      next: (result) => this.unreadCount$.next(result.count),
      error: (err) => console.error('Failed to load unread count:', err)
    });
  }

  /** Get paginated notifications */
  getNotifications(page = 1, pageSize = 20, unreadOnly = false, type?: string): Observable<NotificationPage> {
    let url = `${this.baseUrl}?page=${page}&pageSize=${pageSize}&unreadOnly=${unreadOnly}`;
    if (type) url += `&type=${type}`;
    return this.http.get<NotificationPage>(url);
  }

  /** Mark a single notification as read */
  markAsRead(id: number): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/read`, {});
  }

  /** Mark all notifications as read */
  markAllAsRead(): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/read-all`, {});
  }

  /** Delete a notification */
  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Mark as read locally (optimistic update) */
  markAsReadLocal(id: number): void {
    const current = this.notifications$.value;
    const updated = current.map(n => n.id === id ? { ...n, isRead: true, readAt: new Date().toISOString() } : n);
    this.notifications$.next(updated);
    this.markAsRead(id).subscribe();
  }

  /** Mark all as read locally */
  markAllAsReadLocal(): void {
    const now = new Date().toISOString();
    const current = this.notifications$.value;
    const updated = current.map(n => ({ ...n, isRead: true, readAt: now }));
    this.notifications$.next(updated);
    this.unreadCount$.next(0);
    this.markAllAsRead().subscribe();
  }
}
