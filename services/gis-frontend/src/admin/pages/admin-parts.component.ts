import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { AdminLayoutComponent } from '../components/admin-layout.component';

interface PartCategory {
  id: number;
  name: string;
  description?: string;
  icon?: string;
  partsCount: number;
}

interface VehiclePart {
  id: number;
  categoryId: number;
  name: string;
  description?: string;
  partNumber?: string;
}

@Component({
  selector: 'admin-parts',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Pièces Véhicules">
      <div class="parts-container">
        <!-- Header -->
        <div class="page-header">
          <div class="header-info">
            <h2>Gestion des Pièces</h2>
            <p>Gérez les catégories et les pièces de véhicules</p>
          </div>
          <div class="header-actions">
            <button class="btn-primary" (click)="openCategoryModal()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Nouvelle Catégorie
            </button>
          </div>
        </div>

        <!-- Categories Grid -->
        <div class="categories-grid">
          <div class="category-card" *ngFor="let category of categories" 
               [class.selected]="selectedCategory?.id === category.id"
               (click)="selectCategory(category)">
            <div class="category-header">
              <div class="category-icon">{{ category.icon || '🔧' }}</div>
              <div class="category-actions">
                <button class="btn-icon" (click)="editCategory(category, $event)" title="Modifier">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="btn-icon danger" (click)="deleteCategory(category, $event)" title="Supprimer">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3,6 5,6 21,6"/><path d="M19,6v14a2,2 0 0,1-2,2H7a2,2 0 0,1-2-2V6m3,0V4a2,2 0 0,1,2-2h4a2,2 0 0,1,2,2v2"/>
                  </svg>
                </button>
              </div>
            </div>
            <div class="category-info">
              <h3>{{ category.name }}</h3>
              <p *ngIf="category.description">{{ category.description }}</p>
              <span class="parts-count">{{ category.partsCount }} pièce(s)</span>
            </div>
          </div>

          <!-- Add Category Card -->
          <div class="category-card add-card" (click)="openCategoryModal()">
            <div class="add-icon">+</div>
            <span>Ajouter une catégorie</span>
          </div>
        </div>

        <!-- Parts Section -->
        <div class="parts-section" *ngIf="selectedCategory">
          <div class="section-header">
            <h3>
              <span class="category-badge">{{ selectedCategory.icon || '🔧' }}</span>
              Pièces - {{ selectedCategory.name }}
            </h3>
            <button class="btn-secondary" (click)="openPartModal()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Ajouter une pièce
            </button>
          </div>

          <div class="parts-list" *ngIf="parts.length > 0">
            <div class="part-item" *ngFor="let part of parts">
              <div class="part-info">
                <span class="part-name">{{ part.name }}</span>
                <span class="part-desc" *ngIf="part.description">{{ part.description }}</span>
                <span class="part-number" *ngIf="part.partNumber">Réf: {{ part.partNumber }}</span>
              </div>
              <div class="part-actions">
                <button class="btn-icon" (click)="editPart(part)" title="Modifier">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="btn-icon danger" (click)="deletePart(part)" title="Supprimer">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3,6 5,6 21,6"/><path d="M19,6v14a2,2 0 0,1-2,2H7a2,2 0 0,1-2-2V6m3,0V4a2,2 0 0,1,2-2h4a2,2 0 0,1,2,2v2"/>
                  </svg>
                </button>
              </div>
            </div>
          </div>

          <div class="empty-state" *ngIf="parts.length === 0">
            <div class="empty-icon">📦</div>
            <p>Aucune pièce dans cette catégorie</p>
            <button class="btn-primary" (click)="openPartModal()">Ajouter une pièce</button>
          </div>
        </div>

        <!-- No Category Selected -->
        <div class="no-selection" *ngIf="!selectedCategory && categories.length > 0">
          <div class="empty-icon">👆</div>
          <p>Sélectionnez une catégorie pour voir ses pièces</p>
        </div>
      </div>

      <!-- Category Modal -->
      <div class="modal-overlay" *ngIf="showCategoryModal" (click)="closeCategoryModal()">
        <div class="modal-content" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>{{ editingCategory ? 'Modifier la catégorie' : 'Nouvelle catégorie' }}</h3>
            <button class="btn-close" (click)="closeCategoryModal()">×</button>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>Nom *</label>
              <input type="text" [(ngModel)]="categoryForm.name" placeholder="Ex: Électricité" />
            </div>
            <div class="form-group">
              <label>Description</label>
              <textarea [(ngModel)]="categoryForm.description" placeholder="Description de la catégorie"></textarea>
            </div>
            <div class="form-group">
              <label>Icône (emoji)</label>
              <div class="icon-selector">
                <input type="text" [(ngModel)]="categoryForm.icon" placeholder="🔧" maxlength="2" />
                <div class="icon-suggestions">
                  <button *ngFor="let icon of iconSuggestions" (click)="categoryForm.icon = icon">{{ icon }}</button>
                </div>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn-secondary" (click)="closeCategoryModal()">Annuler</button>
            <button class="btn-primary" (click)="saveCategory()" [disabled]="!categoryForm.name">
              {{ editingCategory ? 'Mettre à jour' : 'Créer' }}
            </button>
          </div>
        </div>
      </div>

      <!-- Part Modal -->
      <div class="modal-overlay" *ngIf="showPartModal" (click)="closePartModal()">
        <div class="modal-content" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>{{ editingPart ? 'Modifier la pièce' : 'Nouvelle pièce' }}</h3>
            <button class="btn-close" (click)="closePartModal()">×</button>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>Nom *</label>
              <input type="text" [(ngModel)]="partForm.name" placeholder="Ex: Radiateur" />
            </div>
            <div class="form-group">
              <label>Description</label>
              <textarea [(ngModel)]="partForm.description" placeholder="Description de la pièce"></textarea>
            </div>
            <div class="form-group">
              <label>Numéro de pièce (optionnel)</label>
              <input type="text" [(ngModel)]="partForm.partNumber" placeholder="Ex: RAD-001" />
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn-secondary" (click)="closePartModal()">Annuler</button>
            <button class="btn-primary" (click)="savePart()" [disabled]="!partForm.name">
              {{ editingPart ? 'Mettre à jour' : 'Créer' }}
            </button>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .parts-container {
      padding: 24px;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }

    .header-info h2 {
      margin: 0;
      font-size: 24px;
      font-weight: 600;
      color: #1f2937;
    }

    .header-info p {
      margin: 4px 0 0;
      color: #6b7280;
      font-size: 14px;
    }

    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      color: white;
      border: none;
      border-radius: 10px;
      font-weight: 600;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    .btn-primary:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .btn-secondary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 16px;
      background: #f1f5f9;
      color: #1f2937;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-weight: 500;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: #e2e8f0;
    }

    .categories-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
      gap: 16px;
      margin-bottom: 32px;
    }

    .category-card {
      background: #fff;
      border: 2px solid #e2e8f0;
      border-radius: 12px;
      padding: 20px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .category-card:hover {
      border-color: #00d4aa;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
    }

    .category-card.selected {
      border-color: #00d4aa;
      background: linear-gradient(135deg, rgba(0, 212, 170, 0.05) 0%, rgba(0, 163, 136, 0.05) 100%);
    }

    .category-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 12px;
    }

    .category-icon {
      font-size: 32px;
    }

    .category-actions {
      display: flex;
      gap: 4px;
      opacity: 0;
      transition: opacity 0.2s;
    }

    .category-card:hover .category-actions {
      opacity: 1;
    }

    .btn-icon {
      width: 28px;
      height: 28px;
      border: none;
      background: #f1f5f9;
      border-radius: 6px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #64748b;
      transition: all 0.2s;
    }

    .btn-icon:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .btn-icon.danger:hover {
      background: #fee2e2;
      color: #dc2626;
    }

    .category-info h3 {
      margin: 0 0 4px;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .category-info p {
      margin: 0 0 8px;
      font-size: 13px;
      color: #6b7280;
    }

    .parts-count {
      font-size: 12px;
      color: #00a388;
      font-weight: 500;
    }

    .add-card {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      border-style: dashed;
      color: #9ca3af;
      min-height: 140px;
    }

    .add-card:hover {
      color: #00a388;
      border-color: #00d4aa;
    }

    .add-icon {
      font-size: 32px;
      margin-bottom: 8px;
    }

    .parts-section {
      background: #fff;
      border-radius: 12px;
      padding: 24px;
      border: 1px solid #e2e8f0;
    }

    .section-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 20px;
    }

    .section-header h3 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #1f2937;
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .category-badge {
      font-size: 24px;
    }

    .parts-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .part-item {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 14px 16px;
      background: #f8fafc;
      border-radius: 8px;
      transition: all 0.2s;
    }

    .part-item:hover {
      background: #f1f5f9;
    }

    .part-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .part-name {
      font-weight: 500;
      color: #1f2937;
    }

    .part-desc {
      font-size: 13px;
      color: #6b7280;
    }

    .part-number {
      font-size: 12px;
      color: #9ca3af;
    }

    .part-actions {
      display: flex;
      gap: 4px;
      opacity: 0;
      transition: opacity 0.2s;
    }

    .part-item:hover .part-actions {
      opacity: 1;
    }

    .empty-state, .no-selection {
      text-align: center;
      padding: 48px 24px;
      color: #6b7280;
    }

    .empty-icon {
      font-size: 48px;
      margin-bottom: 16px;
    }

    .no-selection {
      background: #f8fafc;
      border-radius: 12px;
      border: 2px dashed #e2e8f0;
    }

    /* Modal Styles */
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .modal-content {
      background: #fff;
      border-radius: 16px;
      width: 100%;
      max-width: 480px;
      box-shadow: 0 20px 40px rgba(0, 0, 0, 0.15);
    }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    .modal-header h3 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
    }

    .btn-close {
      width: 32px;
      height: 32px;
      border: none;
      background: #f1f5f9;
      border-radius: 8px;
      font-size: 20px;
      cursor: pointer;
      color: #64748b;
    }

    .btn-close:hover {
      background: #e2e8f0;
    }

    .modal-body {
      padding: 24px;
    }

    .form-group {
      margin-bottom: 16px;
    }

    .form-group label {
      display: block;
      margin-bottom: 6px;
      font-size: 13px;
      font-weight: 500;
      color: #374151;
    }

    .form-group input,
    .form-group textarea {
      width: 100%;
      padding: 10px 14px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 14px;
      transition: all 0.2s;
    }

    .form-group input:focus,
    .form-group textarea:focus {
      outline: none;
      border-color: #00d4aa;
      box-shadow: 0 0 0 3px rgba(0, 212, 170, 0.1);
    }

    .form-group textarea {
      min-height: 80px;
      resize: vertical;
    }

    .icon-selector {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .icon-selector input {
      width: 80px;
      text-align: center;
      font-size: 20px;
    }

    .icon-suggestions {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }

    .icon-suggestions button {
      width: 36px;
      height: 36px;
      border: 1px solid #e2e8f0;
      background: #fff;
      border-radius: 8px;
      font-size: 18px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .icon-suggestions button:hover {
      border-color: #00d4aa;
      background: rgba(0, 212, 170, 0.05);
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
      border-radius: 0 0 16px 16px;
    }
  `]
})
export class AdminPartsComponent implements OnInit {
  categories: PartCategory[] = [];
  parts: VehiclePart[] = [];
  selectedCategory: PartCategory | null = null;

  showCategoryModal = false;
  showPartModal = false;
  editingCategory: PartCategory | null = null;
  editingPart: VehiclePart | null = null;

  categoryForm = { name: '', description: '', icon: '' };
  partForm = { name: '', description: '', partNumber: '' };

  iconSuggestions = ['🔧', '⚡', '🔩', '🛞', '💡', '🔋', '⛽', '🚗', '🛠️', '📦', '🔌', '🪛'];

  constructor(private http: HttpClient, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.loadCategories();
  }

  loadCategories() {
    this.http.get<PartCategory[]>('/api/parts/categories').subscribe({
      next: (categories) => {
        this.categories = categories;
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading categories:', err)
    });
  }

  selectCategory(category: PartCategory) {
    this.selectedCategory = category;
    this.loadParts(category.id);
  }

  loadParts(categoryId: number) {
    this.http.get<VehiclePart[]>(`/api/parts/categories/${categoryId}/parts`).subscribe({
      next: (parts) => {
        this.parts = parts;
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading parts:', err)
    });
  }

  // Category Modal
  openCategoryModal() {
    this.editingCategory = null;
    this.categoryForm = { name: '', description: '', icon: '' };
    this.showCategoryModal = true;
  }

  editCategory(category: PartCategory, event: Event) {
    event.stopPropagation();
    this.editingCategory = category;
    this.categoryForm = {
      name: category.name,
      description: category.description || '',
      icon: category.icon || ''
    };
    this.showCategoryModal = true;
  }

  closeCategoryModal() {
    this.showCategoryModal = false;
    this.editingCategory = null;
  }

  saveCategory() {
    if (!this.categoryForm.name) return;

    const payload = {
      name: this.categoryForm.name,
      description: this.categoryForm.description || null,
      icon: this.categoryForm.icon || null
    };

    if (this.editingCategory) {
      this.http.put(`/api/parts/categories/${this.editingCategory.id}`, payload).subscribe({
        next: () => {
          this.loadCategories();
          this.closeCategoryModal();
        },
        error: (err) => console.error('Error updating category:', err)
      });
    } else {
      this.http.post<PartCategory>('/api/parts/categories', payload).subscribe({
        next: () => {
          this.loadCategories();
          this.closeCategoryModal();
        },
        error: (err) => console.error('Error creating category:', err)
      });
    }
  }

  deleteCategory(category: PartCategory, event: Event) {
    event.stopPropagation();
    if (confirm(`Supprimer la catégorie "${category.name}" et toutes ses pièces ?`)) {
      this.http.delete(`/api/parts/categories/${category.id}`).subscribe({
        next: () => {
          if (this.selectedCategory?.id === category.id) {
            this.selectedCategory = null;
            this.parts = [];
          }
          this.loadCategories();
        },
        error: (err) => console.error('Error deleting category:', err)
      });
    }
  }

  // Part Modal
  openPartModal() {
    this.editingPart = null;
    this.partForm = { name: '', description: '', partNumber: '' };
    this.showPartModal = true;
  }

  editPart(part: VehiclePart) {
    this.editingPart = part;
    this.partForm = {
      name: part.name,
      description: part.description || '',
      partNumber: part.partNumber || ''
    };
    this.showPartModal = true;
  }

  closePartModal() {
    this.showPartModal = false;
    this.editingPart = null;
  }

  savePart() {
    if (!this.partForm.name || !this.selectedCategory) return;

    const payload = {
      categoryId: this.selectedCategory.id,
      name: this.partForm.name,
      description: this.partForm.description || null,
      partNumber: this.partForm.partNumber || null
    };

    if (this.editingPart) {
      this.http.put(`/api/parts/parts/${this.editingPart.id}`, payload).subscribe({
        next: () => {
          this.loadParts(this.selectedCategory!.id);
          this.loadCategories();
          this.closePartModal();
        },
        error: (err) => console.error('Error updating part:', err)
      });
    } else {
      this.http.post<VehiclePart>('/api/parts/parts', payload).subscribe({
        next: () => {
          this.loadParts(this.selectedCategory!.id);
          this.loadCategories();
          this.closePartModal();
        },
        error: (err) => console.error('Error creating part:', err)
      });
    }
  }

  deletePart(part: VehiclePart) {
    if (confirm(`Supprimer la pièce "${part.name}" ?`)) {
      this.http.delete(`/api/parts/parts/${part.id}`).subscribe({
        next: () => {
          this.loadParts(this.selectedCategory!.id);
          this.loadCategories();
        },
        error: (err) => console.error('Error deleting part:', err)
      });
    }
  }
}
