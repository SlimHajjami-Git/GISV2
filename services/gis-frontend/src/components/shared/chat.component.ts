import { Component, OnInit, OnDestroy, ViewChild, ElementRef, ChangeDetectorRef, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { SignalRService } from '../../services/signalr.service';

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

interface AiVehicle {
  id: number;
  name: string;
  plate: string;
  brand: string;
  model: string;
  type: string;
  mileage: number;
  status: string;
}

interface AiMessage {
  id: string;
  role: string;
  content: string;
  timestamp: Date;
  tokensUsed?: number;
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
    <div class="chat-panel" *ngIf="isOpen" [class.show-conversation]="activeUser || aiActiveVehicle">

      <!-- TAB BAR -->
      <div class="tab-bar" *ngIf="!activeUser && !aiActiveVehicle">
        <button class="tab-btn" [class.active]="activeTab === 'chat'" (click)="activeTab = 'chat'">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
          Messages
          <span class="tab-badge" *ngIf="totalUnread > 0">{{ totalUnread }}</span>
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'ai'" (click)="switchToAi()">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2a4 4 0 0 1 4 4v1h1a3 3 0 0 1 3 3v1a3 3 0 0 1-3 3h-1v1a4 4 0 0 1-8 0v-1H7a3 3 0 0 1-3-3v-1a3 3 0 0 1 3-3h1V6a4 4 0 0 1 4-4z"/><circle cx="9" cy="10" r="1"/><circle cx="15" cy="10" r="1"/></svg>
          Diagnostic IA
        </button>
      </div>

      <!-- ═══ MESSAGES TAB ═══ -->
      <div class="chat-sidebar" *ngIf="activeTab === 'chat' && !activeUser">
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
            <p>Aucun utilisateur trouve</p>
          </div>
        </div>
      </div>

      <!-- ═══ AI TAB - Vehicle Selector ═══ -->
      <div class="chat-sidebar" *ngIf="activeTab === 'ai' && !aiActiveVehicle">
        <div class="chat-header ai-header">
          <h3>Diagnostic IA</h3>
          <div class="online-indicator">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2a4 4 0 0 1 4 4v1h1a3 3 0 0 1 3 3v1a3 3 0 0 1-3 3h-1v1a4 4 0 0 1-8 0v-1H7a3 3 0 0 1-3-3v-1a3 3 0 0 1 3-3h1V6a4 4 0 0 1 4-4z"/></svg>
            Llama 3
          </div>
        </div>
        <div class="ai-intro">
          <p>Selectionnez un vehicule pour demarrer un diagnostic intelligent.</p>
        </div>
        <div class="chat-search">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>
          <input type="text" placeholder="Rechercher un vehicule..." [(ngModel)]="aiVehicleSearch" (input)="filterAiVehicles()">
        </div>
        <div class="user-list">
          <div class="user-item" *ngFor="let v of aiFilteredVehicles" (click)="openAiChat(v)">
            <div class="vehicle-avatar" [class]="'vtype-' + v.type">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
            </div>
            <div class="user-info-sm">
              <span class="user-name-sm">{{ v.name }}</span>
              <span class="user-status-sm">{{ v.brand }} {{ v.model }} · {{ v.plate }} · {{ v.mileage | number }} km</span>
            </div>
          </div>
          <div class="empty-users" *ngIf="aiFilteredVehicles.length === 0 && !aiLoadingVehicles">
            <p>Aucun vehicule trouve</p>
          </div>
          <div class="empty-users" *ngIf="aiLoadingVehicles">
            <p>Chargement...</p>
          </div>
        </div>
      </div>

      <!-- ═══ CHAT CONVERSATION (existing) ═══ -->
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
            <p>Demarrez la conversation</p>
          </div>
        </div>
        <div class="message-input">
          <input type="text" placeholder="Ecrire un message..." [(ngModel)]="newMessage" (keydown.enter)="sendMessage()">
          <button class="send-btn" (click)="sendMessage()" [disabled]="!newMessage.trim()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>
          </button>
        </div>
      </div>

      <!-- ═══ AI CONVERSATION ═══ -->
      <div class="chat-conversation" *ngIf="aiActiveVehicle">
        <div class="conv-header ai-conv-header">
          <button class="back-btn" (click)="aiActiveVehicle = null">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"/></svg>
          </button>
          <div class="conv-user">
            <div class="ai-avatar">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2a4 4 0 0 1 4 4v1h1a3 3 0 0 1 3 3v1a3 3 0 0 1-3 3h-1v1a4 4 0 0 1-8 0v-1H7a3 3 0 0 1-3-3v-1a3 3 0 0 1 3-3h1V6a4 4 0 0 1 4-4z"/></svg>
            </div>
            <div class="conv-user-info">
              <span class="conv-user-name">{{ aiActiveVehicle.name }}</span>
              <span class="conv-user-status ai-status">{{ aiActiveVehicle.brand }} {{ aiActiveVehicle.model }} · {{ aiActiveVehicle.mileage | number }} km</span>
            </div>
          </div>
          <button class="clear-btn" (click)="clearAiHistory()" title="Effacer l'historique">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
          </button>
        </div>
        <div class="messages-area" #aiMessagesArea>
          <!-- Welcome message -->
          <div class="ai-welcome" *ngIf="aiMessages.length === 0 && !aiLoading">
            <div class="ai-welcome-icon">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#6366f1" stroke-width="1.5"><path d="M12 2a4 4 0 0 1 4 4v1h1a3 3 0 0 1 3 3v1a3 3 0 0 1-3 3h-1v1a4 4 0 0 1-8 0v-1H7a3 3 0 0 1-3-3v-1a3 3 0 0 1 3-3h1V6a4 4 0 0 1 4-4z"/><circle cx="9" cy="10" r="1"/><circle cx="15" cy="10" r="1"/></svg>
            </div>
            <p class="ai-welcome-title">Diagnostic IA - {{ aiActiveVehicle.name }}</p>
            <p class="ai-welcome-sub">Posez vos questions sur ce vehicule. J'ai acces a son historique complet.</p>
            <div class="ai-suggestions">
              <button class="ai-suggestion" (click)="sendAiSuggestion('Fais un diagnostic complet de ce vehicule')">Diagnostic complet</button>
              <button class="ai-suggestion" (click)="sendAiSuggestion('Quels sont les prochains entretiens a prevoir ?')">Entretiens a prevoir</button>
              <button class="ai-suggestion" (click)="sendAiSuggestion('Analyse la consommation de carburant')">Consommation carburant</button>
              <button class="ai-suggestion" (click)="sendAiSuggestion('Y a-t-il des problemes potentiels a anticiper ?')">Problemes potentiels</button>
            </div>
          </div>
          <!-- AI Messages -->
          <div class="message" *ngFor="let msg of aiMessages" [class.mine]="msg.role === 'user'" [class.other]="msg.role === 'assistant'">
            <div class="msg-bubble" [class.ai-bubble]="msg.role === 'assistant'">
              <div class="ai-content" [innerHTML]="formatAiContent(msg.content)"></div>
              <span class="msg-time">{{ formatTime(msg.timestamp) }}</span>
            </div>
          </div>
          <!-- Typing indicator -->
          <div class="message other" *ngIf="aiLoading">
            <div class="msg-bubble ai-bubble">
              <div class="typing-indicator">
                <span></span><span></span><span></span>
              </div>
            </div>
          </div>
          <!-- Error -->
          <div class="ai-error" *ngIf="aiError">
            <p>{{ aiError }}</p>
          </div>
        </div>
        <div class="message-input">
          <input type="text" placeholder="Posez une question sur ce vehicule..." [(ngModel)]="aiNewMessage" (keydown.enter)="sendAiMessage()" [disabled]="aiLoading">
          <button class="send-btn ai-send" (click)="sendAiMessage()" [disabled]="!aiNewMessage.trim() || aiLoading">
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
      width: 400px; height: 560px;
      background: #fff; border-radius: 16px;
      box-shadow: 0 8px 40px rgba(0,0,0,0.15);
      display: flex; flex-direction: column; overflow: hidden;
      animation: panelIn 0.2s ease;
    }
    @keyframes panelIn { from { opacity:0; transform:translateY(10px); } to { opacity:1; transform:translateY(0); } }

    .chat-sidebar { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
    .chat-panel.show-conversation .chat-sidebar { display: none; }
    .chat-panel.show-conversation .tab-bar { display: none; }
    .chat-conversation { flex: 1; display: flex; flex-direction: column; overflow: hidden; }

    /* Tab Bar */
    .tab-bar {
      display: flex; border-bottom: 1px solid #e2e8f0; background: #f8fafc; flex-shrink: 0;
    }
    .tab-btn {
      flex: 1; display: flex; align-items: center; justify-content: center; gap: 6px;
      padding: 10px; border: none; background: transparent; cursor: pointer;
      font-size: 12px; font-weight: 600; color: #64748b; transition: all 0.15s;
      border-bottom: 2px solid transparent;
    }
    .tab-btn.active { color: #6366f1; border-bottom-color: #6366f1; background: #fff; }
    .tab-btn:hover { background: #f1f5f9; }
    .tab-badge {
      min-width: 16px; height: 16px; border-radius: 8px;
      background: #ef4444; color: #fff; font-size: 9px; font-weight: 700;
      display: flex; align-items: center; justify-content: center; padding: 0 4px;
    }

    /* Header */
    .chat-header {
      padding: 14px 18px; display: flex; justify-content: space-between; align-items: center;
      border-bottom: 1px solid #e2e8f0;
      background: linear-gradient(135deg, #6366f1, #4f46e5); color: #fff;
    }
    .chat-header.ai-header { background: linear-gradient(135deg, #8b5cf6, #6d28d9); }
    .chat-header h3 { margin: 0; font-size: 15px; font-weight: 700; }
    .online-indicator { display: flex; align-items: center; gap: 5px; font-size: 11px; opacity: 0.9; }
    .online-dot { width: 7px; height: 7px; border-radius: 50%; background: #4ade80; }

    /* AI Intro */
    .ai-intro { padding: 10px 16px; background: #faf5ff; border-bottom: 1px solid #f3e8ff; }
    .ai-intro p { margin: 0; font-size: 11px; color: #7c3aed; line-height: 1.4; }

    /* Search */
    .chat-search {
      display: flex; align-items: center; gap: 8px; padding: 10px 14px;
      border-bottom: 1px solid #f1f5f9; flex-shrink: 0;
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
    .vehicle-avatar {
      width: 34px; height: 34px; border-radius: 8px; flex-shrink: 0;
      background: linear-gradient(135deg, #8b5cf6, #6d28d9);
      display: flex; align-items: center; justify-content: center; color: #fff;
    }
    .user-info-sm { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    .user-name-sm { font-size: 13px; font-weight: 600; color: #0f172a; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .user-status-sm { font-size: 10px; color: #94a3b8; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
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
      border-bottom: 1px solid #e2e8f0; background: #f8fafc; flex-shrink: 0;
    }
    .ai-conv-header { background: #faf5ff; border-bottom-color: #f3e8ff; }
    .back-btn {
      width: 30px; height: 30px; border-radius: 6px; border: none;
      background: transparent; color: #64748b; cursor: pointer;
      display: flex; align-items: center; justify-content: center; flex-shrink: 0;
    }
    .back-btn:hover { background: #e2e8f0; }
    .clear-btn {
      margin-left: auto; width: 30px; height: 30px; border-radius: 6px; border: none;
      background: transparent; color: #94a3b8; cursor: pointer;
      display: flex; align-items: center; justify-content: center; flex-shrink: 0;
    }
    .clear-btn:hover { background: #fee2e2; color: #ef4444; }
    .conv-user { display: flex; align-items: center; gap: 10px; min-width: 0; flex: 1; }
    .conv-user-info { display: flex; flex-direction: column; min-width: 0; }
    .conv-user-name { font-size: 13px; font-weight: 600; color: #0f172a; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .conv-user-status { font-size: 10px; color: #94a3b8; }
    .conv-user-status.online { color: #22c55e; }
    .ai-status { color: #7c3aed; }
    .ai-avatar {
      width: 34px; height: 34px; border-radius: 50%; flex-shrink: 0;
      background: linear-gradient(135deg, #8b5cf6, #6d28d9);
      display: flex; align-items: center; justify-content: center; color: #fff;
    }

    /* Messages */
    .messages-area { flex: 1; overflow-y: auto; padding: 14px; display: flex; flex-direction: column; gap: 8px; }
    .messages-date {
      text-align: center; font-size: 10px; color: #94a3b8;
      padding: 4px 12px; background: #f1f5f9; border-radius: 10px;
      align-self: center; margin: 4px 0;
    }
    .message { display: flex; max-width: 85%; }
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
    .msg-bubble.ai-bubble {
      background: linear-gradient(135deg, #faf5ff, #f3e8ff); color: #1e1b4b;
      border: 1px solid #e9d5ff; border-bottom-left-radius: 4px;
    }
    .ai-content { font-size: 13px; line-height: 1.5; word-break: break-word; }
    .ai-content p { margin: 0 0 6px 0; }
    .ai-content p:last-child { margin-bottom: 0; }
    .msg-bubble p { margin: 0; font-size: 13px; line-height: 1.4; word-break: break-word; }
    .msg-time { font-size: 9px; opacity: 0.7; display: block; text-align: right; margin-top: 3px; }
    .no-messages {
      flex: 1; display: flex; flex-direction: column; align-items: center;
      justify-content: center; gap: 8px;
    }
    .no-messages p { font-size: 12px; color: #94a3b8; margin: 0; }

    /* AI Welcome */
    .ai-welcome {
      flex: 1; display: flex; flex-direction: column; align-items: center;
      justify-content: center; gap: 8px; padding: 20px;
    }
    .ai-welcome-icon {
      width: 48px; height: 48px; border-radius: 50%;
      background: linear-gradient(135deg, #ede9fe, #ddd6fe);
      display: flex; align-items: center; justify-content: center;
    }
    .ai-welcome-title { font-size: 14px; font-weight: 700; color: #1e1b4b; margin: 4px 0 0; }
    .ai-welcome-sub { font-size: 11px; color: #64748b; margin: 0; text-align: center; line-height: 1.4; }
    .ai-suggestions { display: flex; flex-wrap: wrap; gap: 6px; justify-content: center; margin-top: 8px; }
    .ai-suggestion {
      padding: 6px 12px; border-radius: 16px; border: 1px solid #e9d5ff;
      background: #faf5ff; color: #7c3aed; font-size: 11px; cursor: pointer;
      transition: all 0.15s;
    }
    .ai-suggestion:hover { background: #ede9fe; border-color: #c4b5fd; }

    /* Typing Indicator */
    .typing-indicator { display: flex; gap: 4px; padding: 4px 0; }
    .typing-indicator span {
      width: 6px; height: 6px; border-radius: 50%; background: #8b5cf6;
      animation: typingBounce 1.4s infinite both;
    }
    .typing-indicator span:nth-child(2) { animation-delay: 0.2s; }
    .typing-indicator span:nth-child(3) { animation-delay: 0.4s; }
    @keyframes typingBounce { 0%,80%,100% { transform: scale(0.6); opacity: 0.4; } 40% { transform: scale(1); opacity: 1; } }

    /* AI Error */
    .ai-error { padding: 8px 12px; background: #fef2f2; border-radius: 8px; border: 1px solid #fecaca; align-self: center; }
    .ai-error p { margin: 0; font-size: 11px; color: #dc2626; }

    /* Message Input */
    .message-input {
      display: flex; align-items: center; gap: 8px; padding: 10px 14px;
      border-top: 1px solid #e2e8f0; background: #f8fafc; flex-shrink: 0;
    }
    .message-input input {
      flex: 1; padding: 8px 12px; border: 1px solid #e2e8f0; border-radius: 20px;
      font-size: 12px; outline: none; background: #fff;
    }
    .message-input input:focus { border-color: #6366f1; }
    .message-input input:disabled { background: #f1f5f9; color: #94a3b8; }
    .send-btn {
      width: 34px; height: 34px; border-radius: 50%; border: none;
      background: #6366f1; color: #fff; cursor: pointer;
      display: flex; align-items: center; justify-content: center;
      transition: background 0.15s; flex-shrink: 0;
    }
    .send-btn:hover { background: #4f46e5; }
    .send-btn:disabled { background: #cbd5e1; cursor: not-allowed; }
    .send-btn.ai-send { background: #8b5cf6; }
    .send-btn.ai-send:hover { background: #7c3aed; }
    .send-btn.ai-send:disabled { background: #cbd5e1; }

    @media (max-width: 480px) {
      .chat-panel { width: calc(100vw - 32px); right: 16px; bottom: 80px; height: 60vh; }
    }
  `]
})
export class ChatComponent implements OnInit, OnDestroy {
  @ViewChild('messagesArea') messagesArea?: ElementRef;
  @ViewChild('aiMessagesArea') aiMessagesArea?: ElementRef;

  isOpen = false;
  activeTab: 'chat' | 'ai' = 'chat';
  activeUser: ChatUser | null = null;
  users: ChatUser[] = [];
  filteredUsers: ChatUser[] = [];
  messages: ChatMessage[] = [];
  newMessage = '';
  searchQuery = '';
  totalUnread = 0;
  loadingMessages = false;

  // AI Chat state
  aiVehicles: AiVehicle[] = [];
  aiFilteredVehicles: AiVehicle[] = [];
  aiActiveVehicle: AiVehicle | null = null;
  aiMessages: AiMessage[] = [];
  aiNewMessage = '';
  aiVehicleSearch = '';
  aiLoading = false;
  aiLoadingVehicles = false;
  aiError = '';

  private currentUserId = 0;
  private refreshInterval: any;
  private chatSub?: Subscription;
  private readSub?: Subscription;

  constructor(
    private apiService: ApiService,
    private signalRService: SignalRService,
    private cdr: ChangeDetectorRef,
    private ngZone: NgZone
  ) {}

  ngOnInit() {
    const storedUser = localStorage.getItem('user');
    if (storedUser) {
      try { this.currentUserId = JSON.parse(storedUser).id || 0; } catch {}
    }

    this.loadUsers();
    this.loadUnreadCount();
    this.setupSignalR();

    this.refreshInterval = setInterval(() => {
      if (this.isOpen) this.loadUsers();
    }, 30000);
  }

  ngOnDestroy() {
    if (this.refreshInterval) clearInterval(this.refreshInterval);
    this.chatSub?.unsubscribe();
    this.readSub?.unsubscribe();
  }

  private setupSignalR() {
    // Listen for incoming chat messages via SignalR
    if (this.signalRService.isConnected()) {
      this.registerChatHandlers();
    }
    // Also try when connection state changes
    this.signalRService.connectionState$.subscribe(state => {
      if (state === 'Connected') {
        this.registerChatHandlers();
      }
    });
  }

  private chatHandlersRegistered = false;
  private registerChatHandlers() {
    if (this.chatHandlersRegistered) return;
    this.chatHandlersRegistered = true;

    // Access the hub connection via the service's internal connection
    // We use a workaround: listen through a custom Subject
    // The SignalR service already registers on the hub, we add ChatMessage handler
    const hub = (this.signalRService as any).hubConnection;
    if (hub) {
      hub.on('ChatMessage', (msg: any) => {
        this.ngZone.run(() => {
          this.handleIncomingMessage(msg);
        });
      });
      hub.on('ChatMessagesRead', (data: any) => {
        this.ngZone.run(() => {
          // Mark messages as read in active conversation
          if (this.activeUser && data.readBy === this.activeUser.id) {
            this.messages.forEach(m => { if (m.isMine) m.isRead = true; });
            this.cdr.detectChanges();
          }
        });
      });
    }
  }

  private handleIncomingMessage(msg: any) {
    const chatMsg: ChatMessage = {
      id: msg.id?.toString() || Date.now().toString(),
      senderId: msg.senderId,
      senderName: msg.senderName,
      receiverId: msg.receiverId,
      content: msg.content,
      timestamp: new Date(msg.timestamp),
      isRead: false,
      isMine: false
    };

    // If conversation with this sender is open, add message and mark as read
    if (this.activeUser && this.activeUser.id === msg.senderId) {
      this.messages.push(chatMsg);
      this.scrollToBottom();
      this.apiService.markChatMessagesRead(msg.senderId).subscribe();
    } else {
      // Update unread count for the sender in user list
      const sender = this.users.find(u => u.id === msg.senderId);
      if (sender) {
        sender.unreadCount = (sender.unreadCount || 0) + 1;
      }
      this.totalUnread++;
    }
    this.cdr.detectChanges();
  }

  loadUsers() {
    this.apiService.getChatUsers().subscribe({
      next: (chatUsers: any[]) => {
        this.ngZone.run(() => {
          this.users = chatUsers.map(u => ({
            id: u.id,
            name: u.name,
            email: u.email,
            isOnline: u.isOnline,
            lastSeen: u.lastSeen ? new Date(u.lastSeen) : undefined,
            unreadCount: u.unreadCount || 0
          }));
          this.totalUnread = this.users.reduce((sum, u) => sum + u.unreadCount, 0);
          this.filterUsers();
          this.cdr.detectChanges();
        });
      },
      error: () => {}
    });
  }

  loadUnreadCount() {
    this.apiService.getChatUnreadCount().subscribe({
      next: (res) => { this.totalUnread = res.count; this.cdr.detectChanges(); },
      error: () => {}
    });
  }

  toggleChat() {
    this.isOpen = !this.isOpen;
    if (this.isOpen) {
      this.activeUser = null;
      this.aiActiveVehicle = null;
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

  // ═══════ AI CHAT METHODS ═══════

  switchToAi() {
    this.activeTab = 'ai';
    if (this.aiVehicles.length === 0) {
      this.loadAiVehicles();
    }
  }

  private loadAiVehicles() {
    this.aiLoadingVehicles = true;
    this.apiService.getAiChatVehicles().subscribe({
      next: (vehicles: any[]) => {
        this.ngZone.run(() => {
          this.aiVehicles = vehicles.map(v => ({
            id: v.id, name: v.name, plate: v.plate || '', brand: v.brand || '',
            model: v.model || '', type: v.type || '', mileage: v.mileage || 0, status: v.status || ''
          }));
          this.aiFilteredVehicles = [...this.aiVehicles];
          this.aiLoadingVehicles = false;
          this.cdr.detectChanges();
        });
      },
      error: () => { this.aiLoadingVehicles = false; this.cdr.detectChanges(); }
    });
  }

  filterAiVehicles() {
    if (!this.aiVehicleSearch) {
      this.aiFilteredVehicles = [...this.aiVehicles];
    } else {
      const q = this.aiVehicleSearch.toLowerCase();
      this.aiFilteredVehicles = this.aiVehicles.filter(v =>
        v.name.toLowerCase().includes(q) || v.plate.toLowerCase().includes(q) ||
        v.brand.toLowerCase().includes(q) || v.model.toLowerCase().includes(q)
      );
    }
  }

  openAiChat(vehicle: AiVehicle) {
    this.aiActiveVehicle = vehicle;
    this.aiMessages = [];
    this.aiError = '';
    this.cdr.detectChanges();

    this.apiService.getAiChatHistory(vehicle.id).subscribe({
      next: (msgs: any[]) => {
        this.ngZone.run(() => {
          this.aiMessages = msgs.map(m => ({
            id: m.id?.toString() || Date.now().toString(),
            role: m.role, content: m.content,
            timestamp: new Date(m.timestamp), tokensUsed: m.tokensUsed
          }));
          this.cdr.detectChanges();
          this.scrollAiToBottom();
        });
      },
      error: () => {}
    });
  }

  sendAiSuggestion(text: string) {
    this.aiNewMessage = text;
    this.sendAiMessage();
  }

  sendAiMessage() {
    if (!this.aiNewMessage.trim() || !this.aiActiveVehicle || this.aiLoading) return;
    const content = this.aiNewMessage.trim();
    this.aiNewMessage = '';
    this.aiError = '';

    this.aiMessages.push({
      id: 'user_' + Date.now(), role: 'user', content, timestamp: new Date()
    });
    this.aiLoading = true;
    this.cdr.detectChanges();
    this.scrollAiToBottom();

    this.apiService.sendAiChatMessage(this.aiActiveVehicle.id, content).subscribe({
      next: (res: any) => {
        this.ngZone.run(() => {
          this.aiMessages.push({
            id: res.messageId?.toString() || Date.now().toString(),
            role: 'assistant', content: res.message,
            timestamp: new Date(), tokensUsed: res.tokensUsed
          });
          this.aiLoading = false;
          this.cdr.detectChanges();
          this.scrollAiToBottom();
        });
      },
      error: (err: any) => {
        this.ngZone.run(() => {
          this.aiLoading = false;
          this.aiError = err.error?.message || 'Erreur lors de la communication avec l\'IA';
          this.cdr.detectChanges();
          this.scrollAiToBottom();
        });
      }
    });
  }

  clearAiHistory() {
    if (!this.aiActiveVehicle) return;
    this.apiService.clearAiChatHistory(this.aiActiveVehicle.id).subscribe({
      next: () => {
        this.aiMessages = [];
        this.aiError = '';
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  formatAiContent(content: string): string {
    return content
      .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
      .replace(/\n/g, '<br>')
      .replace(/- /g, '&bull; ');
  }

  private scrollAiToBottom() {
    setTimeout(() => {
      if (this.aiMessagesArea?.nativeElement) {
        this.aiMessagesArea.nativeElement.scrollTop = this.aiMessagesArea.nativeElement.scrollHeight;
      }
    }, 50);
  }

  openConversation(user: ChatUser) {
    this.activeUser = user;
    this.messages = [];
    this.loadingMessages = true;
    this.cdr.detectChanges();

    // Load messages from API
    this.apiService.getChatMessages(user.id).subscribe({
      next: (msgs: any[]) => {
        this.ngZone.run(() => {
          this.messages = msgs.map(m => ({
            id: m.id?.toString(),
            senderId: m.senderId,
            senderName: m.senderName,
            receiverId: m.receiverId,
            content: m.content,
            timestamp: new Date(m.timestamp),
            isRead: m.isRead,
            isMine: m.isMine
          }));
          this.loadingMessages = false;
          this.cdr.detectChanges();
          this.scrollToBottom();
        });
      },
      error: () => { this.loadingMessages = false; this.cdr.detectChanges(); }
    });

    // Mark messages as read
    if (user.unreadCount > 0) {
      this.totalUnread = Math.max(0, this.totalUnread - user.unreadCount);
      user.unreadCount = 0;
      this.apiService.markChatMessagesRead(user.id).subscribe();
    }
  }

  sendMessage() {
    if (!this.newMessage.trim() || !this.activeUser) return;
    const content = this.newMessage.trim();
    this.newMessage = '';

    // Optimistic UI: add message immediately
    const tempMsg: ChatMessage = {
      id: 'temp_' + Date.now(),
      senderId: this.currentUserId,
      senderName: 'Moi',
      receiverId: this.activeUser.id,
      content,
      timestamp: new Date(),
      isRead: false,
      isMine: true
    };
    this.messages.push(tempMsg);
    this.scrollToBottom();

    // Send to API
    this.apiService.sendChatMessage(this.activeUser.id, content).subscribe({
      next: (savedMsg: any) => {
        // Replace temp message with real one
        const idx = this.messages.findIndex(m => m.id === tempMsg.id);
        if (idx !== -1) {
          this.messages[idx].id = savedMsg.id?.toString();
        }
        this.cdr.detectChanges();
      },
      error: () => {
        // Remove failed message
        const idx = this.messages.findIndex(m => m.id === tempMsg.id);
        if (idx !== -1) this.messages.splice(idx, 1);
        this.cdr.detectChanges();
      }
    });
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
