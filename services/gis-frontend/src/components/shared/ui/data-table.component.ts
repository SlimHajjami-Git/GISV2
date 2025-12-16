import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'ui-data-table',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="table-container">
      <table class="data-table">
        <thead>
          <tr>
            <th *ngFor="let col of columns">{{ col.label }}</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let row of data">
            <td *ngFor="let col of columns">{{ row[col.key] }}</td>
          </tr>
          <tr *ngIf="data.length === 0">
            <td [attr.colspan]="columns.length" class="empty-cell">
              No data available
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .table-container {
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      overflow: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }

    .data-table th {
      padding: 10px 14px;
      text-align: left;
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
      position: sticky;
      top: 0;
    }

    .data-table td {
      padding: 10px 14px;
      color: #1e293b;
      border-bottom: 1px solid #f1f5f9;
    }

    .data-table tbody tr:hover {
      background: #f8fafc;
    }

    .data-table tbody tr:last-child td {
      border-bottom: none;
    }

    .empty-cell {
      text-align: center;
      color: #94a3b8;
      padding: 24px !important;
    }
  `]
})
export class DataTableComponent {
  @Input() columns: { key: string; label: string }[] = [];
  @Input() data: any[] = [];
}
