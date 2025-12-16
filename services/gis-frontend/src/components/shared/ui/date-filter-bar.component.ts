import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'ui-date-filter-bar',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="filter-bar">
      <div class="period-buttons">
        <button 
          *ngFor="let p of periods" 
          [class.active]="selectedPeriod === p.value"
          (click)="selectPeriod(p.value)">
          {{ p.label }}
        </button>
      </div>
      <div class="date-range">
        <span class="date-label">From:</span>
        <input type="date" [(ngModel)]="fromDate" class="date-input" (change)="onDateChange()">
        <span class="date-label">To:</span>
        <input type="date" [(ngModel)]="toDate" class="date-input" (change)="onDateChange()">
        <button class="btn-apply" (click)="apply()">Apply</button>
      </div>
    </div>
  `,
  styles: [`
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 8px 16px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .period-buttons {
      display: flex;
    }

    .period-buttons button {
      padding: 5px 12px;
      background: white;
      border: 1px solid #e2e8f0;
      border-right: none;
      color: #3b82f6;
      font-family: var(--font-family);
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .period-buttons button:first-child {
      border-radius: 3px 0 0 3px;
    }

    .period-buttons button:last-child {
      border-radius: 0 3px 3px 0;
      border-right: 1px solid #e2e8f0;
    }

    .period-buttons button.active {
      background: #3b82f6;
      border-color: #3b82f6;
      color: white;
    }

    .period-buttons button:hover:not(.active) {
      background: #f8fafc;
    }

    .date-range {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .date-label {
      color: #64748b;
      font-size: 12px;
    }

    .date-input {
      padding: 4px 8px;
      background: white;
      border: none;
      color: #3b82f6;
      font-family: var(--font-family);
      font-size: 12px;
      text-decoration: underline;
      cursor: pointer;
    }

    .btn-apply {
      padding: 5px 14px;
      background: #3b82f6;
      border: none;
      border-radius: 3px;
      color: white;
      font-family: var(--font-family);
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-apply:hover {
      background: #2563eb;
    }

    @media (max-width: 768px) {
      .filter-bar {
        flex-wrap: wrap;
      }
    }
  `]
})
export class DateFilterBarComponent {
  @Input() selectedPeriod = 'week';
  @Input() fromDate = '';
  @Input() toDate = '';
  @Input() periods = [
    { value: 'today', label: 'Today' },
    { value: 'yesterday', label: 'Yesterday' },
    { value: 'week', label: 'Week' },
    { value: 'month', label: 'Month' }
  ];

  @Output() periodChange = new EventEmitter<string>();
  @Output() dateRangeChange = new EventEmitter<{ from: string; to: string }>();
  @Output() applyFilter = new EventEmitter<void>();

  selectPeriod(period: string) {
    this.selectedPeriod = period;
    const today = new Date();
    
    switch (period) {
      case 'today':
        this.fromDate = this.toDate = today.toISOString().split('T')[0];
        break;
      case 'yesterday':
        const yesterday = new Date(today.getTime() - 24 * 60 * 60 * 1000);
        this.fromDate = this.toDate = yesterday.toISOString().split('T')[0];
        break;
      case 'week':
        const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
        this.fromDate = weekAgo.toISOString().split('T')[0];
        this.toDate = today.toISOString().split('T')[0];
        break;
      case 'month':
        const monthAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);
        this.fromDate = monthAgo.toISOString().split('T')[0];
        this.toDate = today.toISOString().split('T')[0];
        break;
    }
    
    this.periodChange.emit(period);
    this.dateRangeChange.emit({ from: this.fromDate, to: this.toDate });
  }

  onDateChange() {
    this.dateRangeChange.emit({ from: this.fromDate, to: this.toDate });
  }

  apply() {
    this.applyFilter.emit();
  }
}
