import { Component, OnInit, OnDestroy, ViewChild, ElementRef, ChangeDetectorRef, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';

interface ChatUser {
  id: number;
  name: string;
  email: string;
  isOnline: boolean;
  lastSeen?: Date;
  unreadCount: number;
}

interface ChatMessage {
  id: string;
  senderId: number;
  senderName: string;
  receiverId: number;
  content: string;
  timestamp: Date;
  isRead: boolean;
  isMine: boolean;
}

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <!-- Floating Chat Button -->
    <button class="chat-fab" (click)="toggleChat()" [class.has-unread]="totalUnread > 0">
      <svg *ngIf="!isOpen" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>
      </svg>
      <svg *ngIf="isOpen" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
        <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
      </svg>
      <span class="unread-badge" *ngIf="totalUnread > 0 && !isOpen">{{ totalUnread }}</span>
    </button>

    <!-- Chat Panel -->
    <div class="chat-panel" *ngIf="isOpen" [class.show-conversation]="activeUser">
      <!-- User List -->
      <div class="chat-sidebar">
        <div class="chat-header">
          <h3>Messages</h3>
          <div class="online-indicator">
            <span class="online-dot"></span>
            {{ getOnlineCount() }} en ligne
          </div>
        </div>
        <div class="chat-search">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>
          <input type="text" placeholder="Rechercher..." [(ngModel)]="searchQuery" (input)="filterUsers()">
        </div>
        <div class="user-list">
          <div class="user-item" *ngFor="let user of filteredUsers" (click)="openConversation(user)" [class.active]="activeUser?.id === user.id" [class.unread]="user.unreadCount > 0">
            <div class="user-avatar-sm" [class.online]="user.isOnline">
              {{ getInitials(user.name) }}
            </div>
            <div class="user-info-sm">
              <span class="user-name-sm">{{ user.name }}</span>
              <span class="user-status-sm">{{ user.isOnline ? 'En ligne' : 'Hors ligne' }}</span>
            </div>
            <span class="unread-count" *ngIf="user.unreadCount > 0">{{ user.unreadCount }}</span>
          </div>
          <div class="empty-users" *ngIf="filteredUsers.length === 0">
            <p>Aucun utilisateur trouvé</p>
          </div>
        </div>
      </div>

      <!-- Conversation -->
      <div class="chat-conversation" *ngIf="activeUser">
        <div class="conv-header">
          <button class="back-btn" (click)="activeUser = null">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"/></svg>
          </button>
          <div class="conv-user">
            <div class="user-avatar-sm" [class.online]="activeUser.isOnline">
              {{ getInitials(activeUser.name) }}
            </div>
            <div class="conv-user-info">
              <span class="conv-user-name">{{ activeUser.name }}</span>
              <span class="conv-user-status" [class.online]="activeUser.isOnline">
                {{ activeUser.isOnline ? 'En ligne' : 'Hors ligne' }}
              </span>
            </div>
          </div>
        </div>
        <div class="messages-area" #messagesArea>
          <div class="messages-date">Aujourd'hui</div>
          <div class="message" *ngFor="let msg of messages" [class.mine]="msg.isMine" [class.other]="!msg.isMine">
            <div class="msg-bubble">
              <p>{{ msg.content }}</p>
              <span class="msg-time">{{ formatTime(msg.timestamp) }}</span>
            </div>
          </div>
          <div class="no-messages" *ngIf="messages.length === 0">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5">
              <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>
            </svg>
            <p>Démarrez la conversation</p>
          </div>
        </div>
        <div class="message-input">
          <input type="text" placeholder="Écrire un message..." [(ngModel)]="newMessage" (keydown.enter)="sendMessage()">
          <button class="send-btn" (click)="sendMessage()" [disabled]="!newMessage.trim()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    /* Floating Action Button */
    .chat-fab {
      position: fixed; bottom: 24px; right: 24px; z-index: 999;
      width: 52px; height: 52px; border-radius: 50%; border: none;
      background: linear-gradient(135deg, #6366f1, #4f46e5);
      color: #fff; cursor: pointer; display: flex; align-items: center; justify-content: center;
      box-shadow: 0 4px 16px rgba(99,102,241,0.4);
      transition: all 0.2s;
    }
    .chat-fab:hover { transform: scale(1.05); box-shadow: 0 6px 20px rgba(99,102,241,0.5); }
    .chat-fab.has-unread { animation: fabPulse 2s infinite; }
    @keyframes fabPulse { 0%,100% { box-shadow: 0 4px 16px rgba(99,102,241,0.4); } 50% { box-shadow: 0 4px 24px rgba(99,102,241,0.7); } }
    .unread-badge {
      position: absolute; top: -4px; right: -4px;
      min-width: 20px; height: 20px; border-radius: 10px;
      background: #ef4444; color: #fff; font-size: 11px; font-weight: 700;
      display: flex; align-items: center; justify-content: center;
      border: 2px solid #fff; padding: 0 4px;
    }

    /* Chat Panel */
    .chat-panel {
      position: fixed; bottom: 88px; right: 24px; z-index: 998;
      width: 380px; height: 520px;
      background: #fff; border-radius: 16px;
      box-shadow: 0 8px 40px rgba(0,0,0,0.15);
      display: flex; overflow: hidden;
      animation: panelIn 0.2s ease;
    }
    @keyframes panelIn { from { opacity:0; transform:translateY(10px); } to { opacity:1; transform:translateY(0); } }

    .chat-sidebar { width: 100%; display: flex; flex-direction: column; }
    .chat-panel.show-conversation .chat-sidebar { display: none; }
    .chat-conversation { width: 100%; display: flex; flex-direction: column; }

    /* Header */
    .chat-header {
      padding: 16px 18px; display: flex; justify-content: space-between; align-items: center;
      border-bottom: 1px solid #e2e8f0;
      background: linear-gradient(135deg, #6366f1, #4f46e5); color: #fff;
      border-radius: 16px 16px 0 0;
    }
    .chat-header h3 { margin: 0; font-size: 15px; font-weight: 700; }
    .online-indicator { display: flex; align-items: center; gap: 5px; font-size: 11px; opacity: 0.9; }
    .online-dot { width: 7px; height: 7px; border-radius: 50%; background: #4ade80; }

    /* Search */
    .chat-search {
      display: flex; align-items: center; gap: 8px; padding: 10px 14px;
      border-bottom: 1px solid #f1f5f9;
    }
    .chat-search svg { color: #94a3b8; flex-shrink: 0; }
    .chat-search input {
      flex: 1; border: none; outline: none; font-size: 12px; color: #334155;
      background: transparent;
    }
    .chat-search input::placeholder { color: #94a3b8; }

    /* User List */
    .user-list { flex: 1; overflow-y: auto; }
    .user-item {
      display: flex; align-items: center; gap: 10px; padding: 10px 14px;
      cursor: pointer; transition: background 0.1s; border-bottom: 1px solid #f8fafc;
    }
    .user-item:hover { background: #f8fafc; }
    .user-item.active { background: #ede9fe; }
    .user-item.unread { background: #fefce8; }
    .user-avatar-sm {
      width: 34px; height: 34px; border-radius: 50%; flex-shrink: 0;
      background: linear-gradient(135deg, #94a3b8, #64748b);
      display: flex; align-items: center; justify-content: center;
      font-size: 11px; font-weight: 700; color: #fff; text-transform: uppercase;
      position: relative;
    }
    .user-avatar-sm.online { background: linear-gradient(135deg, #6366f1, #4f46e5); }
    .user-avatar-sm.online::after {
      content: ''; position: absolute; bottom: 0; right: 0;
      width: 10px; height: 10px; border-radius: 50%;
      background: #22c55e; border: 2px solid #fff;
    }
    .user-info-sm { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    .user-name-sm { font-size: 13px; font-weight: 600; color: #0f172a; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .user-status-sm { font-size: 10px; color: #94a3b8; }
    .unread-count {
      min-width: 18px; height: 18px; border-radius: 9px;
      background: #6366f1; color: #fff; font-size: 10px; font-weight: 700;
      display: flex; align-items: center; justify-content: center; padding: 0 4px;
    }
    .empty-users { padding: 30px; text-align: center; }
    .empty-users p { font-size: 12px; color: #94a3b8; margin: 0; }

    /* Conversation Header */
    .conv-header {
      display: flex; align-items: center; gap: 8px; padding: 10px 14px;
      border-bottom: 1px solid #e2e8f0; background: #f8fafc;
    }
    .back-btn {
      width: 30px; height: 30px; border-radius: 6px; border: none;
      background: transparent; color: #64748b; cursor: pointer;
      display: flex; align-items: center; justify-content: center;
    }
    .back-btn:hover { background: #e2e8f0; }
    .conv-user { display: flex; align-items: center; gap: 10px; }
    .conv-user-info { display: flex; flex-direction: column; }
    .conv-user-name { font-size: 13px; font-weight: 600; color: #0f172a; }
    .conv-user-status { font-size: 10px; color: #94a3b8; }
    .conv-user-status.online { color: #22c55e; }

    /* Messages */
    .messages-area { flex: 1; overflow-y: auto; padding: 14px; display: flex; flex-direction: column; gap: 8px; }
    .messages-date {
      text-align: center; font-size: 10px; color: #94a3b8;
      padding: 4px 12px; background: #f1f5f9; border-radius: 10px;
      align-self: center; margin: 4px 0;
    }
    .message { display: flex; max-width: 80%; }
    .message.mine { align-self: flex-end; }
    .message.other { align-self: flex-start; }
    .msg-bubble {
      padding: 8px 12px; border-radius: 12px; position: relative;
    }
    .message.mine .msg-bubble {
      background: linear-gradient(135deg, #6366f1, #4f46e5); color: #fff;
      border-bottom-right-radius: 4px;
    }
    .message.other .msg-bubble {
      background: #f1f5f9; color: #0f172a;
      border-bottom-left-radius: 4px;
    }
    .msg-bubble p { margin: 0; font-size: 13px; line-height: 1.4; word-break: break-word; }
    .msg-time { font-size: 9px; opacity: 0.7; display: block; text-align: right; margin-top: 3px; }
    .no-messages {
      flex: 1; display: flex; flex-direction: column; align-items: center;
      justify-content: center; gap: 8px;
    }
    .no-messages p { font-size: 12px; color: #94a3b8; margin: 0; }

    /* Message Input */
    .message-input {
      display: flex; align-items: center; gap: 8px; padding: 10px 14px;
      border-top: 1px solid #e2e8f0; background: #f8fafc;
    }
    .message-input input {
      flex: 1; padding: 8px 12px; border: 1px solid #e2e8f0; border-radius: 20px;
      font-size: 12px; outline: none; background: #fff;
    }
    .message-input input:focus { border-color: #6366f1; }
    .send-btn {
      width: 34px; height: 34px; border-radius: 50%; border: none;
      background: #6366f1; color: #fff; cursor: pointer;
      display: flex; align-items: center; justify-content: center;
      transition: background 0.15s; flex-shrink: 0;
    }
    .send-btn:hover { background: #4f46e5; }
    .send-btn:disabled { background: #cbd5e1; cursor: not-allowed; }

    @media (max-width: 480px) {
      .chat-panel { width: calc(100vw - 32px); right: 16px; bottom: 80px; height: 60vh; }
    }
  `]
})
export class ChatComponent implements OnInit, OnDestroy {
  @ViewChild('messagesArea') messagesArea?: ElementRef;

  isOpen = false;
  activeUser: ChatUser | null = null;
  users: ChatUser[] = [];
  filteredUsers: ChatUser[] = [];
  messages: ChatMessage[] = [];
  newMessage = '';
  searchQuery = '';
  totalUnread = 0;

  private currentUserId = 0;
  private refreshInterval: any;

  constructor(
    private apiService: ApiService,
    private cdr: ChangeDetectorRef,
    private ngZone: NgZone
  ) {}

  ngOnInit() {
    this.loadUsers();
    // Refresh user list every 30 seconds
    this.refreshInterval = setInterval(() => {
      if (this.isOpen) this.loadUsers();
    }, 30000);
  }

  ngOnDestroy() {
    if (this.refreshInterval) clearInterval(this.refreshInterval);
  }

  loadUsers() {
    this.apiService.getUsers().subscribe({
      next: (apiUsers: any[]) => {
        this.ngZone.run(() => {
          this.users = apiUsers.map(u => ({
            id: u.id,
            name: u.name,
            email: u.email,
            isOnline: u.status === 'active',
            unreadCount: 0
          }));
          this.filterUsers();
          this.cdr.detectChanges();
        });
      },
      error: () => {}
    });
  }

  toggleChat() {
    this.isOpen = !this.isOpen;
    if (this.isOpen) {
      this.activeUser = null;
      this.loadUsers();
    }
  }

  filterUsers() {
    if (!this.searchQuery) {
      this.filteredUsers = [...this.users];
    } else {
      const q = this.searchQuery.toLowerCase();
      this.filteredUsers = this.users.filter(u =>
        u.name.toLowerCase().includes(q) || u.email.toLowerCase().includes(q)
      );
    }
  }

  openConversation(user: ChatUser) {
    this.activeUser = user;
    user.unreadCount = 0;
    this.messages = [];
    // In a real implementation, load messages from API/SignalR here
    this.scrollToBottom();
  }

  sendMessage() {
    if (!this.newMessage.trim() || !this.activeUser) return;
    const msg: ChatMessage = {
      id: Date.now().toString(),
      senderId: this.currentUserId,
      senderName: 'Moi',
      receiverId: this.activeUser.id,
      content: this.newMessage.trim(),
      timestamp: new Date(),
      isRead: false,
      isMine: true
    };
    this.messages.push(msg);
    this.newMessage = '';
    this.scrollToBottom();

    // Simulate a reply after 1-2 seconds
    setTimeout(() => {
      if (this.activeUser) {
        const reply: ChatMessage = {
          id: (Date.now() + 1).toString(),
          senderId: this.activeUser.id,
          senderName: this.activeUser.name,
          receiverId: this.currentUserId,
          content: this.getAutoReply(),
          timestamp: new Date(),
          isRead: true,
          isMine: false
        };
        this.ngZone.run(() => {
          this.messages.push(reply);
          this.cdr.detectChanges();
          this.scrollToBottom();
        });
      }
    }, 1000 + Math.random() * 1500);
  }

  private getAutoReply(): string {
    const replies = [
      'Bien reçu, merci !',
      'OK, je m\'en occupe.',
      'D\'accord, à tout à l\'heure.',
      'Parfait, merci pour l\'info.',
      'Je vérifie et je reviens vers vous.',
      'Noté 👍',
      'Compris, je vous tiens au courant.'
    ];
    return replies[Math.floor(Math.random() * replies.length)];
  }

  private scrollToBottom() {
    setTimeout(() => {
      if (this.messagesArea?.nativeElement) {
        this.messagesArea.nativeElement.scrollTop = this.messagesArea.nativeElement.scrollHeight;
      }
    }, 50);
  }

  getInitials(name: string): string {
    return name.split(' ').map(n => n[0]).join('').substring(0, 2);
  }

  getOnlineCount(): number {
    return this.users.filter(u => u.isOnline).length;
  }

  formatTime(date: Date): string {
    return new Date(date).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }
}
