import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'ui-button',
  standalone: true,
  imports: [CommonModule],
  template: `
    <button 
      [class]="'btn btn-' + variant + ' btn-' + size"
      [disabled]="disabled || loading"
      (click)="onClick.emit($event)">
      <span class="loader" *ngIf="loading"></span>
      <ng-content></ng-content>
    </button>
  `,
  styles: [`
    .btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      font-family: var(--font-family);
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
      border: none;
      white-space: nowrap;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    /* Variants */
    .btn-primary {
      background: #3b82f6;
      color: white;
      border: 1px solid #3b82f6;
    }
    .btn-primary:hover:not(:disabled) {
      background: #2563eb;
    }

    .btn-secondary {
      background: white;
      color: #64748b;
      border: 1px solid #e2e8f0;
    }
    .btn-secondary:hover:not(:disabled) {
      background: #f8fafc;
      color: #1e293b;
    }

    .btn-ghost {
      background: transparent;
      color: #3b82f6;
      border: none;
    }
    .btn-ghost:hover:not(:disabled) {
      background: #f1f5f9;
    }

    .btn-link {
      background: transparent;
      color: #3b82f6;
      border: none;
      padding: 0;
    }
    .btn-link:hover:not(:disabled) {
      text-decoration: underline;
    }

    /* Sizes */
    .btn-sm {
      padding: 4px 10px;
      font-size: 12px;
      border-radius: 3px;
    }

    .btn-md {
      padding: 6px 14px;
      font-size: 13px;
      border-radius: 4px;
    }

    .btn-lg {
      padding: 10px 20px;
      font-size: 14px;
      border-radius: 6px;
    }

    /* Loader */
    .loader {
      width: 14px;
      height: 14px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: white;
      border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `]
})
export class ButtonComponent {
  @Input() variant: 'primary' | 'secondary' | 'ghost' | 'link' = 'primary';
  @Input() size: 'sm' | 'md' | 'lg' = 'md';
  @Input() disabled = false;
  @Input() loading = false;
  @Output() onClick = new EventEmitter<MouseEvent>();
}
