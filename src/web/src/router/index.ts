import { createRouter, createWebHistory, type RouteLocationNormalized } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import AboutView from '@/views/AboutView.vue'
import ArticlesView from '@/views/ArticlesView.vue'
import ArticleDetailView from '@/views/ArticleDetailView.vue'
import BlogView from '@/views/BlogView.vue'
import EventsView from '@/views/EventsView.vue'
import ResourcesView from '@/views/ResourcesView.vue'
import NewsletterView from '@/views/NewsletterView.vue'
import LoginView from '@/views/LoginView.vue'
import NotFoundView from '@/views/NotFoundView.vue'
import { useAuthStore } from '@/stores/auth'

// Auth-gated views are dynamically imported so TipTap (used only by EditorView),
// the Approvals queue, and the dashboard never ship to anonymous public visitors.
// Each import becomes its own chunk via Vite's code-splitting.
const AuthCallbackView = () => import('@/views/AuthCallbackView.vue')
const DashboardView    = () => import('@/views/DashboardView.vue')
const EditorView       = () => import('@/views/EditorView.vue')
const EventManagementView   = () => import('@/views/EventManagementView.vue')

declare module 'vue-router' {
  interface RouteMeta {
    /** Redirect to /login if the user isn't signed in. */
    requiresAuth?: boolean
    /** Roles that may access the route. Logical OR. Implies requiresAuth. */
    requiresRole?: string[]
  }
}

const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: (to, _from, savedPosition) => {
    if (savedPosition) return savedPosition
    // Honour deep links like /blog#post-<id>. The 96px offset clears the
    // sticky header. If the target isn't in the DOM yet (async-loaded lists),
    // the destination view scrolls to it once its data arrives.
    if (to.hash) return { el: to.hash, top: 96, behavior: 'smooth' }
    return { top: 0 }
  },
  routes: [
    { path: '/',                  name: 'home',            component: HomeView },
    { path: '/about',             name: 'about',           component: AboutView },
    { path: '/articles',          name: 'articles',        component: ArticlesView },
    { path: '/articles/:slug',    name: 'article-detail',  component: ArticleDetailView },
    { path: '/blog',              name: 'blog',            component: BlogView },
    { path: '/events',            name: 'events',          component: EventsView },
    { path: '/events/previous',  name: 'events-previous', component: () => import('@/views/EventsPreviousView.vue') },
    { path: '/events/:id',       name: 'event-detail',    component: () => import('@/views/EventDetailView.vue') },
    { path: '/resources',         name: 'resources',       component: ResourcesView },
    { path: '/newsletter',        name: 'newsletter',      component: NewsletterView },
    { path: '/login',             name: 'login',           component: LoginView },
    { path: '/auth/callback',     name: 'auth-callback',   component: AuthCallbackView },

    { path: '/dashboard', name: 'dashboard', component: DashboardView,
      meta: { requiresAuth: true } },
    { path: '/editor', name: 'editor', component: EditorView,
      meta: { requiresAuth: true, requiresRole: ['Admin', 'Contributor'] } },
    { path: '/admin/members', name: 'admin-members', component: () => import('@/views/MemberManagementView.vue'),
      meta: { requiresAuth: true, requiresRole: ['Admin'] } },
    { path: '/admin/content', name: 'admin-content', component: () => import('@/views/ManageContentView.vue'),
      meta: { requiresAuth: true, requiresRole: ['Admin', 'Contributor'] } },
    { path: '/admin/resources', name: 'admin-resources', component: () => import('@/views/ResourceManagementView.vue'),
      meta: { requiresAuth: true, requiresRole: ['Admin'] } },
    { path: '/admin/approvals', name: 'admin-approvals', component: () => import('@/views/ApprovalsView.vue'),
      meta: { requiresAuth: true, requiresRole: ['Admin'] } },
    { path: '/admin/events', name: 'admin-events', component: EventManagementView,
      meta: { requiresAuth: true, requiresRole: ['Admin', 'Contributor'] } },

    { path: '/:pathMatch(.*)*',   name: 'not-found',       component: NotFoundView },
  ],
})

router.beforeEach(async (to: RouteLocationNormalized) => {
  if (!to.meta.requiresAuth && !to.meta.requiresRole) return true

  const auth = useAuthStore()
  await auth.initialize()

  if (!auth.isAuthenticated) {
    return { name: 'login', query: { returnTo: to.fullPath } }
  }
  if (to.meta.requiresRole && !to.meta.requiresRole.some(r => auth.roles.includes(r))) {
    return { name: 'home', query: { forbidden: to.fullPath } }
  }
  return true
})

export default router
