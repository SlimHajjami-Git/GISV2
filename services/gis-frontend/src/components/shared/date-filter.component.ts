import { Component, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-date-filter',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="date-filter-bar">
      <div class="period-buttons">
        <button
          *ngFor="let period of periods"
          class="period-btn"
          [class.active]="selectedPeriod === period.value"
          (click)="selectPeriod(period.value)">
          {{ period.label }}
        </button>
      </div>

      <div class="date-inputs">
        <div class="input-group">
          <label>From</label>
          <input type="datetime-local" [(ngModel)]="fromDate" class="date-input" />
        </div>
        <div class="input-group">
          <label>To</label>
          <input type="datetime-local" [(ngModel)]="toDate" class="date-input" />
        </div>
      </div>

      <div class="filter-actions">
        <label class="checkbox-label">
          <input type="checkbox" [(ngModel)]="includeCurrent" />
          <span>Including current</span>
        </label>
        <button class="execute-btn" (click)="onExecute()">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polygon points="5 3 19 12 5 21 5 3"/>
          </svg>
          Execute
        </button>
      </div>
    </div>
  `,
  styles: [`
    .date-filter-bar {
      background: var(--bg-secondary);
      border-bottom: 1px solid var(--border-color);
      padding: 16px 20px;
      display: flex;
      align-items: center;
      gap: 20px;
      flex-wrap: wrap;
    }

    .period-buttons {
      display: flex;
      gap: 8px;
    }

    .period-btn {
      padding: 8px 16px;
      border-radius: 6px;
      border: 1px solid var(--border-color);
      background: var(--bg-tertiary);
      color: var(--text-secondary);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .period-btn:hover {
      background: var(--bg-hover);
      color: var(--text-primary);
    }

    .period-btn.active {
      background: var(--primary);
      border-color: var(--primary);
      color: white;
    }

    .date-inputs {
      display: flex;
      gap: 12px;
      flex: 1;
    }

    .input-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .input-group label {
      font-size: 12px;
      font-weight: 500;
      color: var(--text-secondary);
    }

    .date-input {
      padding: 8px 12px;
      border-radius: 6px;
      border: 1px solid var(--border-color);
      background: var(--bg-tertiary);
      color: var(--text-primary);
      font-size: 13px;
    }

    .date-input:focus {
      outline: none;
      border-color: var(--primary);
    }

    .filter-actions {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      color: var(--text-secondary);
      cursor: pointer;
    }

    .execute-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 20px;
      border-radius: 6px;
      border: none;
      background: var(--primary);
      color: white;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .execute-btn:hover {
      background: var(--primary-dark);
    }
  `]
})
export class DateFilterComponent {
  @Output() onFilter = new EventEmitter<any>();

  periods = [
    { label: 'Today', value: 'today' },
    { label: 'Yesterday', value: 'yesterday' },
    { label: 'Week', value: 'week' },
    { label: 'Month', value: 'month' }
  ];

  selectedPeriod = 'today';
  fromDate = '';
  toDate = '';
  includeCurrent = false;

  ngOnInit() {
    this.selectPeriod('today');
  }

  selectPeriod(period: string) {
    this.selectedPeriod = period;
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    switch (period) {
      case 'today':
        this.fromDate = this.toDateTime(today);
        this.toDate = this.toDateTime(now);
        break;
      case 'yesterday':
        const yesterday = new Date(today);
        yesterday.setDate(today.getDate() - 1);
        this.fromDate = this.toDateTime(yesterday);
        this.toDate = this.toDateTime(today);
        break;
      case 'week':
        const weekAgo = new Date(today);
        weekAgo.setDate(today.getDate() - 7);
        this.fromDate = this.toDateTime(weekAgo);
        this.toDate = this.toDateTime(now);
        break;
      case 'month':
        const monthAgo = new Date(today);
        monthAgo.setMonth(today.getMonth() - 1);
        this.fromDate = this.toDateTime(monthAgo);
        this.toDateTime(now);
        break;
    }
  }

  toDateTime(date: Date): string {
    return date.toISOString().slice(0, 16);
  }

  onExecute() {
    this.onFilter.emit({
      fromDate: this.fromDate,
      toDate: this.toDate,
      includeCurrent: this.includeCurrent
    });
  }
}
